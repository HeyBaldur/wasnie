# Diagnóstico READ-ONLY — qué significa realmente `Payee.IsActive`

**Fecha:** 2026-08-28 · **Rama:** AI-CHAT-ASSISTANT · **Sin commit** · **Sin cambios de producto, sin
escrituras, sin migraciones.**

---

## ★ Primero y aparte (§5): **`Payee.IsActive` NO gobierna acceso ni autenticación**

El WI pedía que si el flag tocaba login se reportara primero. **No lo toca.**

Los dos únicos `IsActive` del camino de autenticación —`LoginCommandHandler.cs:45` y
`VerifyTwoFactorLoginCommandHandler.cs:42`— son **`tenant.IsActive`**, el flag del *Tenant*, no el del
Payee. Son entidades distintas y flags distintos.

Y `Wasnie.Application/Features/Auth/` **no menciona la palabra `Payee` en ninguna línea**: el subsistema
de autenticación no conoce el concepto. Un payee sin `UserId` vinculado no puede entrar de todos modos, y
ese vínculo es `Payee.UserId`, no `IsActive`.

> **No hay frente de acceso.** Todo lo que sigue es financiero y operativo.

---

# PASO 2 — El mapa

## 2.1 — Quién lo ESCRIBE

| # | dónde | qué hace |
|---|---|---|
| 1 | `Payee.cs:80` (`Payee.Create`) | nace en **`true`** |
| 2 | `Payee.cs:206-214` (`Deactivate`) | → `false`, y sella `DeactivatedAt = now` |
| 3 | `Payee.cs:216-224` (`Activate`) | → `true`, y **borra `DeactivatedAt`** |
| 4 | `PayeeConfiguration.cs:27` + migración `P2_PayeeLifecycle` | `HasDefaultValue(true)` en la base |

`Deactivate`/`Activate` tienen **un solo llamador**: `DeactivateActivatePayeeHandler.cs:25` y `:45`,
ambos tras `Permission.PayeesDeactivate`.

**Y esto es lo que NO lo escribe, verificado:**

- **`MarkAsTerminated` (`Payee.cs:194-201`)** — sólo `Status` y `TerminationDate`. Es el hallazgo que
  motiva el WI.
- **`Payee.Update` (`:88-130`)** — no lo menciona.
- **Importación y sincronización con HubSpot** — cero coincidencias de `IsActive` en
  `Wasnie.Infrastructure/Integrations` ni en los handlers de import.

> **Nada automático lo pone en `false`, nunca.** Un solo camino humano, explícito y con permiso propio.
> No es un flag que derive solo: es un flag que casi nadie usa.

## 2.2 — Quién lo LEE — **la parte central**

| dominio | lector | qué hace con él |
|---|---|---|
| **Alta de transacciones** | `IngestTransactionHandler.cs:52` | **RECHAZA** el alta: *"Payee is inactive. Use import to assign historical transactions to inactive payees."* ★ **el único lector que bloquea algo** |
| **Importación** | `TransactionImportValidationService.cs:128` | **AVISO**, no rechazo (Decisión 12) |
| **Actualización por Excel** | `TransactionUpdateValidationService.cs:220` | ídem, aviso |
| **Dashboard** | `GetDashboardSummaryHandler.cs:481-482` | dos contadores, activos / inactivos |
| **UI — listado** | `payees-list.component.html:97,141,160` | chip "Inactive"; alterna Desactivar/Activar |
| **UI — detalle** | `payee-detail.component.html:52,71,81` | ídem |
| **API** | `PayeeDto.cs:24` | lo expone |

**Y esto es lo que NO lo lee, verificado uno por uno:**

- **`CreditAllocationService`** — cero. Los `IsActive` de ese archivo son de **reglas** (`r.IsActive`), no
  de personas.
- **El motor de pay runs** (`CalculatePayoutsForPeriodHandler`) — usa **`PayeeStatus.Terminated`**.
- **Autenticación / portal** — nada (ver arriba).
- **Sincronización con el CRM** — nada.
- **Filtro global de EF** — `ApplicationDbContext.cs:121` filtra **sólo por `TenantId`**. No hay borrado
  lógico oculto: un payee inactivo aparece en todas las consultas como cualquier otro.
- **Informes / exportaciones** — nada más allá de los dos contadores del dashboard.

> **`IsActive` gobierna exactamente UNA cosa funcional: si se puede dar de alta una transacción nueva
> para esa persona por la vía manual/API.** Todo lo demás es decoración informativa (un chip, dos
> contadores) o un aviso que no frena nada.

## 2.3 — ★ Qué se rompería si un terminado pasara a `IsActive = false`

| lector | efecto del cambio |
|---|---|
| `IngestTransactionHandler:52` | **el único efecto real:** dejarían de poder darse de alta transacciones nuevas para esa persona por la vía manual/API. **La importación sigue funcionando** — el propio mensaje de error lo dice y esa es su válvula de escape |
| Validación de import / update | aparecería una fila de **aviso**. El import no se detiene |
| `CreditAllocationService` | **nada.** No lo lee |
| Motor de pay runs | **nada.** Lee `Status` |
| Créditos ya existentes | **nada.** Ninguno se recalcula, se supersede ni se excluye |
| Balance, ledger, cola de huérfanas | **nada** |
| Dashboard | los contadores activos/inactivos se mueven en uno |
| UI | un chip "Inactive" de más y aparece el botón Activar |
| Login / visibilidad / CRM | **nada** |

> **Veredicto: el flag es SEGURO de manipular.** Su radio de acción es la puerta de entrada de datos
> nuevos, no el dinero ya devengado. Apagarlo en un terminado no le quita a nadie un euro.

**★ Pero hay una trampa que conviene conocer antes de tocarlo:** **la UI no ofrece ninguna forma de
desactivar a un terminado.** El botón Desactivar sólo se renderiza cuando `status === OnLeave`
(`payee-detail.component.html:71`, `payees-list.component.html:141`). El **endpoint, en cambio, no tiene
esa guarda**: `DeactivateActivatePayeeHandler:18-28` comprueba permiso y existencia, y nada más.

Así que hoy `Terminated + IsActive = false` se alcanza **sólo por API**, o desactivando *antes* de
terminar. Y la UI **sí** contempla la combinación (`payee-detail.component.html:81` ofrece Activar a un
terminado inactivo), así que no es un estado imposible: es uno que la interfaz sabe mostrar pero no sabe
producir.

## 2.4 — Relación con `Status`: **no hay contradicción en el código**

Lectores de `PayeeStatus`: el motor de pay runs (`:75-96`), la cola de huérfanas
(`ListTerminatedPayeesWithBalanceHandler:64`), y la UI de payees (badges y qué botones ofrecer).

**No encontré una sola línea que asuma que "terminado ⇒ inactivo" ni al revés.** Al contrario: el
comentario del dominio (`Payee.cs:39-42`) declara la ortogonalidad — *"Platform assignment eligibility
(Decision G). **Orthogonal to PayeeStatus** (HR status)"* — y la UI la respeta mostrando los dos
distintivos por separado.

**El único desajuste real es de INTENCIÓN, no de coherencia:** el camino de alta de transacciones guarda
sobre `IsActive` cuando la pregunta que importa para el dinero es `Status`. Nadie se contradice; es que
la guarda está puesta sobre el eje que no responde esa pregunta.

## 2.5 — Estado real de los datos

| `Status` | `IsActive` | payees |
|---|---|---|
| Active | 1 | **161** |
| OnLeave | 1 | 2 |
| **Terminated** | **0** | **1** |
| **Terminated** | **1** | **6** |

Total 170. `DeactivatedAt` no nulo: **1** (el mismo).

> **No hay combinaciones "imposibles".** Bajo el diseño ortogonal las cuatro son legítimas. Ni siquiera
> `Terminated + IsActive = 1` es deriva: es **el resultado por defecto** de terminar a alguien, porque
> `MarkAsTerminated` no toca el otro eje. **6 de 7 no es un accidente repetido — es el comportamiento.**
>
> La combinación `Active + IsActive = 0` no ocurre, pero sería legítima: alguien en plantilla a quien no
> se le deben asignar tratos nuevos.

## 2.6 — El origen: **la hipótesis del WI está al revés**

| columna | migración | fecha |
|---|---|---|
| `Status` + `TerminationDate` | `20260525114324_ExtendPayeeQuotaAssignment` | **25-may-2026** |
| `IsActive` + `DeactivatedAt` | `20260601123007_P2_PayeeLifecycle` (`defaultValue: true`) | **1-jun-2026** |

*(El `IsActive` que aparece en `AddCompensationContext` del 22-may es de la tabla **`PlanRules`**, no de
`Payees`.)*

> **`Status` es el concepto VIEJO; `IsActive` llegó una semana DESPUÉS** — lo contrario de "un concepto
> viejo y uno nuevo que nunca se reconciliaron". Y llegó **deliberadamente como un segundo eje**,
> etiquetado *"Decision G"* y documentado como ortogonal en el mismo commit.
>
> **No es deriva: es diseño.** Lo que sí quedó sin reconciliar es una sola guarda — la de alta de
> transacciones — que eligió el eje nuevo para una pregunta que vive en el viejo.

---

# PASO 3 — Las cuatro preguntas

## 1. ¿Es seguro de manipular? — **Sí**

No gobierna login, ni visibilidad, ni sincronización, ni filtros globales, ni el cálculo. Su único efecto
funcional es cerrar la puerta del alta manual/API de transacciones nuevas, y esa puerta tiene una válvula
documentada (la importación sigue permitida, con aviso).

**Con dos advertencias:**
- **Activar borra la evidencia.** `Activate()` pone `DeactivatedAt = null`: el hecho de que estuvo
  desactivado **desaparece**. Si se empieza a usar el flag como parte de un proceso, esa amnesia importa.
- **La UI no lo puede apagar en un terminado** (§2.3), así que cualquier uso operativo hoy es por API.

## 2. ¿Hacen falta banderas independientes? — **Sí, y de hecho ya son tres preguntas distintas**

| eje | pregunta que responde | existe |
|---|---|---|
| `Status` | ¿esta persona sigue en la empresa? | ✅ |
| `IsActive` | ¿se le puede atribuir trabajo nuevo? | ✅ |
| **`ClosedAt`** (futuro) | **¿su cuenta financiera está saldada?** | ❌ |

Son ortogonales de verdad: alguien terminado puede tener la cuenta abierta (6 casos hoy) o cerrada, y
alguien en plantilla podría no admitir tratos nuevos. **`Status` + `ClosedAt` alcanzan para la historia
financiera; `IsActive` responde otra cosa y debe quedarse aparte.** Fusionarlos sería perder la pregunta
que hoy sí contesta.

## 3. ★ ¿Hay créditos que no deberían existir? — **NO. Ninguno.**

Bajo la política decidida (aceptar los créditos de terminados), **los 46 créditos de payees terminados
son válidos**. Medido:

| | |
|---|---|
| créditos de payees hoy `IsActive = 0` | **0** |
| créditos de payees `Terminated` | 46 |
| …asignados **después** de `TerminationDate` | **0** |
| …asignados **en** la fecha de terminación | **1** (POL-8554) |

> **No hace falta revisar datos históricos.** Ésa era la respuesta que el WI esperaba y se confirma.

**Un caso que sí merece nombrarse, y no es un crédito inválido sino una transacción cuya premisa no se
sostiene sola:** `TERM-CC-10`, €5.000, **fechada 2026-10-10** para *Test Terminacion CC* (TERM-CC-01),
**terminado el 2026-09-30**. La política *"se devenga cuando se cierra el trato"* justifica un crédito
tardío sobre una venta **anterior** a la salida; ésta es una venta **posterior**. Es la única fila así, y
el payee se llama literalmente "Test Terminacion CC", así que **casi con seguridad es dato de prueba** —
pero es la forma exacta que la política no cubre, y conviene decidir qué se hace si aparece de verdad.
*(Supuesto: que es dato de prueba. No establecido con certeza.)*

## 4. ¿Qué guarda debería usar el motor? — recomendación, sin construir

| momento | guarda recomendada | por qué |
|---|---|---|
| **Alta / atribución de créditos** | **ninguna que bloquee** — un **aviso** sobre `Status == Terminated` | La política es aceptar. Y ya existe el patrón: el import avisa sin frenar. Hoy la terminación es **invisible** en el alta, y ése es el defecto real — no que falte un bloqueo, sino que nadie se entera |
| **Elegibilidad en pay runs** | **`Status`** — ya es la correcta | Está bien hoy (`:75-96`) |
| **Cola de huérfanas** | `Status` **+ el futuro `ClosedAt`** | Cerrar una cuenta debe sacarla de la cola; hoy la pertenencia se deriva sólo del dinero |
| **`IsActive`** | **dejarlo donde está** | Responde su propia pregunta y la responde bien |

**No recomiendo tocar `MarkAsTerminated` para que apague `IsActive`.** Sería acoplar dos ejes que el
producto separó a propósito, y su único efecto sería bloquear el alta manual de transacciones de gente
que se fue — que es exactamente lo que la política decidida **permite**.

---

# Medido vs. supuesto vs. no establecido

## Medido
- `Payee.IsActive` no aparece en ningún camino de autenticación; los dos hits son `Tenant.IsActive`.
- Cuatro escritores; un solo llamador humano; ni `MarkAsTerminated`, ni `Update`, ni import, ni HubSpot
  lo tocan.
- Siete lectores; **uno solo bloquea algo** (`IngestTransactionHandler:52`).
- El filtro global de EF sobre `Payee` es sólo `TenantId`.
- Matriz `Status` × `IsActive`: 161 / 2 / 1 / 6 sobre 170.
- `Status` (25-may) precede a `IsActive` (1-jun, `defaultValue: true`).
- 46 créditos de terminados, 0 posteriores a la fecha de terminación, 1 en la fecha.
- La UI no ofrece desactivar a un terminado; el endpoint no lo impide.
- `Activate()` borra `DeactivatedAt`.

## Supuesto
- Que `TERM-CC-10` es dato de prueba (por el nombre del payee).
- Que la ausencia de coincidencias en los barridos negativos (auth, CRM, informes) es completa: busqué
  por `IsActive` literal, así que un lector que lo copiara antes a otra variable se me escaparía.
- La recomendación del Paso 3.4.

## No establecido
- **Por qué se introdujo "Decision G"** más allá de lo que dice su comentario: no hay documento de
  decisión en `docs/` que lo desarrolle, y el historial de git no lo explica.
- Si algún tenant real usa `IsActive` operativamente: sólo miré dev, donde 1 de 170 lo tiene apagado.
- Qué debería pasar con una venta **fechada** después de la salida (el caso `TERM-CC-10`).

---

# Recomendación sobre el WI de cierre de cuenta

> **Se puede construir encima de lo que hay. No hace falta reconciliar los dos flags antes.**

Los tres motivos:

1. **`IsActive` no interfiere.** No lo lee el motor, ni la cola, ni el ledger, ni el cálculo. Un WI de
   cierre puede ignorarlo por completo.
2. **`Status` ya es la guarda correcta** donde importa, y es la que el cierre debería extender con
   `ClosedAt`.
3. **No hay datos que corregir** (Paso 3.3), así que el WI no arrastra una migración de saneamiento
   además de la de esquema.

**Lo que sí conviene resolver primero, y es barato:** que la terminación sea **visible** en el alta de
transacciones —un aviso, no un bloqueo—, para que el próximo POL-8554 no sea una sorpresa de 56 segundos.
Eso no depende de `IsActive` ni del cierre, y es el defecto que este diagnóstico deja expuesto.

---

**Sin cambios de producto. Sin escrituras. Sin migraciones.** — para revisión de Rodolfo.
