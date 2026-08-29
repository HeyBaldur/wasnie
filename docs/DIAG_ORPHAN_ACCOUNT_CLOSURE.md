# Diagnóstico READ-ONLY — cierre de cuentas huérfanas

**Fecha:** 2026-08-28 · **Rama:** AI-CHAT-ASSISTANT · **Sin commit** · **Sin cambios de producto, sin
escrituras en la base, sin migraciones generadas.**

---

## ★ PASO 2 — Qué hace HOY el botón: **NADA. La puerta de parada NO se dispara.**

Esto va primero y aparte, como pedía el WI.

`WasnieUi/src/app/features/ledger/terminated/terminated-accounts.component.html:104-113`:

```html
@if ('Ledger.Adjust' | hasPermission) {
  <ws-button variant="secondary" size="sm"
    [routerLink]="['/payees', row.payeeId]" [queryParams]="{ tab: 'ledger' }">
    {{ 'LEDGER.TERMINATED_SETTLE' | translate }}   <!-- en.json:2012 → "Close account" -->
  </ws-button>
}
```

**Es un `routerLink` y nada más.** Sin `(click)`, sin llamada a API, sin handler. El componente
(`terminated-accounts.component.ts`) no tiene un solo método que escriba: sólo `load()` y dos helpers de
formato, y no inyecta ningún servicio de escritura. `ws-button` no tiene efectos propios más allá de
propagar el `routerLink`.

**El botón lleva a la pestaña Ledger del payee.** El cierre real se escribe ahí, con el flujo de ajuste
manual que ya existía antes del WI 1 — que es exactamente lo que el comentario del template dice, y es
verdad: *"there is deliberately no second write path"*.

> **No hay un botón activo sobre dinero sin diseño acordado.** El nombre "Close account" promete más de
> lo que el control hace — es un enlace etiquetado como una acción — pero no escribe nada. Es un
> problema de rótulo, no de seguridad.

**El único camino de escritura, y su ceremonia actual:**
`CreateManualLedgerAdjustmentHandler.cs:40` exige `Permission.LedgerAdjust`. La pantalla del payee pide
confirmación por modal. **Reversible: no** — el ledger es append-only (ver §3.3).

---

# PASO 3 — El dominio: qué existe y qué falta

## 3.1 — Marcar un crédito como no pagado: **NO EXISTE. Hace falta migración.**

`WasnieApi/src/Wasnie.Domain/Compensation/Credits/Credit.cs`. Un crédito tiene exactamente **dos**
formas de salir de circulación, y **ninguna sirve**:

| mecanismo | file:line | qué significa | por qué NO sirve |
|---|---|---|---|
| `Consume(Guid payoutId, …)` | `:92-101` | un payout **pagado** lo consumió | **`payoutId` no es nullable.** Y `Unconsume` (`:104-115`) hace `ConsumedByPayoutId!.Value` sin comprobar: un `Guid.Empty` inventado dejaría el rastro anti-doble-pago apuntando a un payout que no existe, y un revert posterior lo "devolvería" a la nada |
| `Supersede(reason, …)` | `:75-88` | fue **reemplazado** por una reatribución | Es otro hecho de negocio. Emite `CreditSupersededEvent`, y las consultas de attainment lo leen como "stale, hay otro que lo sustituye" — cuando en un write-off no hay ningún otro. Además **es irreversible**: no hay `Unsupersede` |

**No hay `Forfeited`, no hay `WrittenOff`, no hay estado alguno de "vivo pero nunca se va a pagar".**

> **★★ RESPUESTA EXPLÍCITA: SÍ, HACE FALTA MIGRACIÓN.** Un `WrittenOff` necesita columnas nuevas en
> `Credits` (como mínimo `WrittenOffAt` + quién/por qué, y probablemente un vínculo al asiento de cierre),
> un método de dominio nuevo, y que `CalculatePayoutsForPeriodHandler:201-209` sume la condición al par
> de nulls que ya filtra. **Reutilizar `Consume` o `Supersede` corrompe un significado existente** — y
> los dos son consultados por otros subsistemas, así que la corrupción no queda contenida.

## 3.2 — Tipos de asiento del ledger: **YA EXISTEN LOS TRES QUE HACEN FALTA**

`WasnieApi/src/Wasnie.Domain/Compensation/Enums/LedgerEnums.cs:22-96` — nueve tipos:

| # | tipo | signo | sirve para cerrar |
|---|---|---|---|
| 0 | `ClawbackDebit` | − | no (crea la deuda) |
| 1 | `ClawbackForgivenessCredit` | + | decisión de negocio: perdonar deuda real |
| 2 | `ManualBonusCredit` | + | no |
| 3 | `DataCorrectionDebit` | − | no |
| 4 | `ClawbackAppliedCredit` | + | no (lo escribe el pay run) |
| **5** | **`ExternalSettlementCredit`** | **+** | **★ deuda recuperada FUERA de Wasnie (nómina, RRHH)** |
| **6** | **`WriteOffCredit`** | **+** | **★ la empresa absorbe la pérdida** |
| 7 | `DataCorrectionCredit` | + | error TÉCNICO, deliberadamente distinto de perdonar |
| **8** | **`FinalSettlementDebit`** | **−** | **★ se le PAGÓ al saliente lo que se le debía, fuera de Wasnie** |

**El lado del ledger está completo y bien pensado.** El propio enum explica por qué 5 y 6 son
tipos separados: *"un CFO tiene que poder totalizar cada uno sin minar texto libre"*. Y el 8 responde la
pregunta espejo: cuánto efectivo salió hacia gente que ya no está.

> **No hacen falta tipos nuevos** para llevar una deuda a cero. Lo que falta es el lado de los
> **créditos** (§3.1), que es una tabla distinta y un problema distinto.

## 3.3 — Reversibilidad

| mecanismo | ¿se deshace? | ¿queda rastro? |
|---|---|---|
| Asiento de ledger | **NO.** Cero `Remove`/`RemoveRange` sobre `PayeeLedgerEntries` en todo `src` | Sí — es append-only por diseño; un error se corrige con un asiento **contrario**, que también queda |
| `Credit.Consume` | **Sí** — `Unconsume`, usada al revertir un payout pagado | Sí, vía eventos de dominio |
| `Credit.Supersede` | **NO** — no existe `Unsupersede` | Sí (`SupersededAt` + `SupersededBy`, motivo obligatorio, ≤500 chars) |
| `Payee.Deactivate` | **Sí** — `Activate()` limpia `DeactivatedAt` | Parcial: al reactivar, `DeactivatedAt` se pone a `null` y **el hecho desaparece** |

**Consecuencia para el diseño:** un cierre que escriba un asiento **no se puede deshacer**, sólo
compensar con otro. Eso está bien y es coherente con el resto del ledger — pero significa que la
ceremonia de confirmación tiene que estar a la altura, porque no hay "deshacer".

## 3.4 — La bandera de cierre: **NO EXISTE**

`Payee` tiene `Status` (`Active` / `OnLeave` / `Terminated`), `TerminationDate`, `IsActive` y
`DeactivatedAt`. **Ninguna es un `ClosedAt`.**

**★ `IsActive` NO sirve y confundirlo sería un error caro.** Su comentario en `Payee.cs:39-42` es
explícito: *"Platform assignment eligibility (Decision G). **Orthogonal to PayeeStatus** (HR status).
When false, new transactions cannot be assigned"*. Es sobre **entrada de datos**, no sobre liquidación,
y es reversible sin dejar rastro.

Además, hoy la pertenencia a la cola **se deriva** (`Terminated` + balance ≠ 0 **o** créditos sin
liquidar): no hay bandera que consultar. Añadir un `ClosedAt` cambiaría eso de derivado a declarado, que
es una decisión de diseño con consecuencias (una cuenta "cerrada" con un crédito nuevo encima quedaría
invisible otra vez — exactamente el agujero que cerró el WI 1).

---

# ★ PASO 4 — Trazabilidad

## 4.1 — Sí existe entidad reutilizable, **y el handler que cierra cuentas YA la usa**

`WasnieApi/src/Wasnie.Domain/Audit/AuditLog.cs` — con `BeforeJson`, `AfterJson`, `Metadata`,
`ActorUserId`, `ActorEmail`, `TimestampUtc`, `CorrelationId`, `IpAddress`.

Y `CreateManualLedgerAdjustmentHandler.cs:86-101` **ya escribe una entrada rica** por cada cierre:

```csharp
Action: AuditActions.LedgerAdjustmentCreated,
DisplayName: $"{type} {entry.Amount} — {payee.FullName}",
Metadata: { transactionType, signedAmount, currency, balanceAfter, justification }
```

**Así que el importe cerrado, la resolución elegida, el usuario, la fecha y la nota YA se guardan hoy.**
Contra lo que suponía el WI, no es *"un booleano y un párrafo"*: es una fila de auditoría tipada con el
importe con signo y el balance resultante.

**Lo único que falta es "qué se cerró" a nivel de CRÉDITOS** — y falta por una razón de fondo, no por
descuido: **el flujo de hoy no toca créditos**. Sólo mueve el balance del ledger. En cuanto un cierre
incluya `WrittenOff` sobre créditos concretos, los ids van en el mismo `Metadata`, sin infraestructura
nueva.

**Dos detalles no obvios de la infraestructura, ambos a favor:**

- **`IMoneyCriticalCommand` existe** (`AuditBehavior.cs:45-60`): en esa ruta, **si falla la auditoría se
  revierte la escritura de negocio** — misma transacción, `CommitAsync` de las dos mitades. Es
  exactamente la garantía que un cierre de cuenta debería tener.
- **Hay dos caminos de auditoría** y el del ledger usa el segundo: `IAuditableCommand` (interceptado por
  el behavior, **no** permite `Metadata` porque `BuildEntry` no lo puebla) e `IAuditService` inyectado
  directo en el handler (**sí** permite `Metadata`, y es el que usa el ajuste manual). Sólo 8 comandos
  usan el primero.

## 4.2 — Qué haría falta

**Nada de infraestructura.** Sólo poblar el `Metadata` existente con los ids de los créditos cerrados y
su importe individual. **No construido.**

*(Supuesto, no medido: `Metadata` es `Dictionary<string,string>`, así que una lista de ids iría como
string — habría que decidir si eso es suficiente para un auditor o si merece una columna tipada. No lo
resuelvo acá.)*

---

# ★★ PASO 5 — Concurrencia

## 5.1 — Sí hay concurrencia optimista, en tres entidades

| entidad | token | file:line |
|---|---|---|
| **`Credit`** | `byte[] RowVersion` → `IsRowVersion()` (rowversion real de SQL Server) | `Credit.cs:29`, `CreditConfiguration.cs:40` |
| **`PayeeBalance`** | ídem | `PayeeBalance.cs:39`, `PayeeBalanceConfiguration.cs:31` |
| `HubSpotConnection` | ídem | `HubSpotConnection.cs:63` |

## 5.2 — ★ Y acá está el hallazgo que decide entre las dos opciones

> **`Payee` NO TIENE token de concurrencia.** Ni `RowVersion`, ni `IsConcurrencyToken`, ni columna en
> `PayeeConfiguration`. Confirmado también contra el esquema de la base.

**Así que "versionar la fila del payee" no es una opción débil: hoy directamente no existe**, y
adoptarla exigiría su propia migración — además de que, como el WI ya observa, protegería la fila
equivocada. El payee puede estar intacto mientras entra un crédito nuevo.

Las dos opciones, sin elegir:

### Opción A — huella del conjunto

Versionar la agregación mostrada (p. ej. hash de los `RowVersion` de los créditos incluidos, o
`max(RowVersion)` + conteo).

- **A favor:** `Credit.RowVersion` **ya existe** y es un rowversion real, así que la huella se calcula
  sin migración y detecta cualquier cambio en cualquier fila del conjunto — incluidos los que no cambian
  el importe total.
- **En contra:** el error que produce es opaco (*"algo cambió"*) y no dice **qué**. Y hay que definir la
  huella con cuidado: un `max(RowVersion)` no detecta una **eliminación**, y un conteo solo no detecta
  una sustitución.

### Opción B — ids explícitos

El cliente manda los ids exactos que el usuario vio; el cierre **falla** si el conjunto actual no
coincide.

- **A favor:** el error es diagnosticable (*"apareció el crédito X"*), y la semántica es la más honesta
  posible para dinero: **se cierra lo que se mostró, o no se cierra**. No necesita ninguna columna nueva.
- **En contra:** el payload crece con el conjunto, y hay que decidir qué es "no coincide" — ¿un crédito
  nuevo invalida el cierre de los otros, o se cierran los que siguen ahí? Esa decisión es de producto,
  no técnica.

**Observación mía, para que la decisión no se tome a ciegas:** las dos son compatibles con lo que hay y
ninguna necesita migración. La B además cubre el caso que la A no distingue bien — una fila borrada — y
da un mensaje que un humano puede accionar.

## 5.3 — ★ ¿Puede aparecer un crédito nuevo para un payee terminado? **SÍ. Establecido en código y medido.**

**En código, dos caminos, ninguno con guarda:**

1. **`MarkAsTerminated` (`Payee.cs:194-201`) sólo escribe `Status` y `TerminationDate`.** **No toca
   `IsActive`.**
2. **`CreditAllocationService` no consulta `PayeeStatus` ni el `IsActive` del payee** en ninguna línea
   (los `IsActive` que aparecen ahí son de **reglas**, no de personas).
3. **`ReassignPayeeHandler.cs:31-33`** comprueba que el payee **exista** y nada más. Sin `IsActive`, sin
   `Status`. **Éste es el camino exacto de POL-8554.**
4. `IngestTransactionHandler.cs:52` sí bloquea `!payee.IsActive` — pero como terminar **no** desactiva,
   esa guarda no protege a un terminado.

**Medido en la base de dev:**

| | |
|---|---|
| payees terminados | 7 |
| **…cuyo `IsActive` sigue en 1** | **6** |
| créditos asignados **en** la fecha de terminación de su payee | **1** (POL-8554) |
| créditos asignados en fecha **posterior** | 0 |

*(La anomalía de los 56 segundos es intra-día, por eso cae en la fila "en la fecha" y no en
"posterior": termination 15:16:41 → crédito 15:17:37, ambos 2026-08-27.)*

> **Consecuencia directa para el diseño:** el conjunto de créditos de un payee terminado **no es
> estable**, ni siquiera en teoría. La concurrencia del §5.2 no es una precaución formal — es el escenario
> real que ya ocurrió una vez.

---

# PASO 6 — Permisos y estados

## 6.1 — Leer y cerrar **ya son permisos distintos**, pero los tienen los mismos roles

| | permiso | dónde |
|---|---|---|
| ver la cola (ruta) | `Ledger.Read` | `app.routes.ts:81` (`hasPermissionGuard`) |
| ver la cola (query) | `Permission.LedgerRead` | `ListTerminatedPayeesWithBalanceHandler.cs:33` |
| ver el botón | `Ledger.Adjust` | `terminated-accounts.component.html:104` |
| **escribir el cierre** | `Permission.LedgerAdjust` | `CreateManualLedgerAdjustmentHandler.cs:40` |

La separación **existe**. Lo que no existe es una diferencia **de rol**: `RolePermissions.cs` da
`Ledger.Read` **y** `Ledger.Adjust` a los mismos dos roles (`TenantAdmin`, `CompManager`), así que hoy
todo el que puede mirar puede castigar. Un permiso propio de cierre sería una constante nueva en
`Permission.cs` más su entrada en el mapa de roles — barato, y una decisión de producto.

## 6.2 — La vara: `Mark as paid`

- **Permiso propio:** `Permission.PayoutsMarkPaid` (`MarkPayRunPaidHandler.cs:29`) — no reutiliza
  `Payouts.Read`.
- **Confirmación reforzada** (`pay-run-detail.component.html:465-500`): modal dedicado que muestra el
  **conteo** de payouts, los **totales por moneda**, un aviso de los que se saltan, y el texto
  *"This action cannot be undone."* (`en.json:1598`).

> **El cierre debería tener al menos eso**, y hoy tiene menos: el ajuste manual del ledger confirma, pero
> sin mostrar el desglose de qué se está cerrando. Y a diferencia de `Mark as paid`, un cierre puede
> significar **dar por perdido** dinero, no sólo declararlo pagado.

## 6.3 — ★ El estado intermedio: **NO EXISTE, y el WI tiene razón en la consecuencia**

`PayeeStatus` es `Active` / `OnLeave` / `Terminated`. No hay *en negociación*, *congelado* ni *en
disputa*, ni en `Payee` ni en `Credit` ni en el ledger.

Y la pertenencia a la cola **se deriva** del dinero: `Terminated` **y** (balance ≠ 0 **o** créditos sin
liquidar). No hay bandera intermedia que consultar.

> **Confirmado: con sólo "liquidado" y "castigado", la única forma de sacar de la lista un finiquito en
> litigio es escribir un asiento contable que afirma algo falso** — un `ExternalSettlementCredit` que
> dice que se recuperó dinero que no se recuperó, o un `WriteOffCredit` que dice que se absorbió una
> pérdida que todavía se está peleando. En un ledger append-only, esa mentira **no se puede borrar
> después**: sólo compensar con otro asiento, dejando dos hechos falsos en el historial.

`Payee.Deactivate()` **no** es ese estado: bloquea la asignación de transacciones nuevas, es ortogonal al
estado de RRHH, y al reactivar borra `DeactivatedAt` sin dejar rastro.

---

# Medido vs. supuesto vs. no establecido

## Medido
- El botón "Close account" es un `routerLink`; el componente no tiene ninguna ruta de escritura.
- `Credit` no tiene estado de write-off; `Consume` exige un `payoutId` no nullable y `Unconsume` lo
  desreferencia sin comprobar; `Supersede` es irreversible y significa otra cosa.
- Los tres tipos de ledger para cerrar (5, 6, 8) ya existen.
- `PayeeLedgerEntries` no se borra en ninguna línea de `src`.
- `Payee` no tiene `ClosedAt`; `IsActive` es ortogonal y reversible sin rastro.
- `AuditLog` + `IAuditService` ya registran cada ajuste con importe, moneda, balance resultante y nota;
  `IMoneyCriticalCommand` ya ofrece rollback si falla la auditoría.
- `Credit` y `PayeeBalance` tienen rowversion real; **`Payee` no tiene ninguno**.
- Termination no toca `IsActive`; ni `CreditAllocationService` ni `ReassignPayeeHandler` miran el estado
  del payee. 6 de 7 terminados siguen con `IsActive = 1`; 1 crédito se asignó el día de la terminación.
- `Ledger.Read` y `Ledger.Adjust` son permisos distintos con los mismos dos roles.
- `Mark as paid` = permiso propio + modal con conteo, totales por moneda y aviso de irreversibilidad.
- No existe estado intermedio en ninguna de las tres entidades.

## Supuesto (razonado, no ejecutado)
- Que un `Guid.Empty` en `ConsumedByPayoutId` corrompería el rastro anti-doble-pago: leído del código
  (`Unconsume` hace `!.Value`), **no** probado con una escritura.
- Que `Metadata` como `Dictionary<string,string>` alcanza para una lista de ids de crédito: es la forma
  que tiene, no una validación de que a un auditor le sirva.
- El tamaño estimado abajo.

## No establecido
- **Si un `ClosedAt` declarado es mejor que la pertenencia derivada de hoy.** Tiene un riesgo concreto
  (una cuenta cerrada con un crédito nuevo encima vuelve a ser invisible) y no lo resolví.
- **Qué debe pasar si el conjunto cambió parcialmente** (opción B): ¿falla todo o se cierra lo que
  queda? Es decisión de producto.
- Si la anomalía de los 56 segundos fue una reatribución deliberada o un accidente de datos de prueba —
  sigue sin establecerse desde el diagnóstico anterior.
- Si algún tenant real tiene cuentas huérfanas: sólo miré la base de dev.

---

# Estimación honesta del WI de construcción

**Más grande de lo que el diseño que circuló sugiere, y el motivo es §3.1.**

| pieza | tamaño | por qué |
|---|---|---|
| **Migración de `Credits`** (`WrittenOffAt` + autor + motivo + vínculo al asiento) | **M** | Es la pieza que convierte esto en un WI con esquema. Sobre la tabla más caliente del producto |
| Método de dominio `WriteOff` + su evento + guardas | S | El patrón de `Supersede`/`Consume` está ahí para copiar |
| Sumar la condición al motor (`:201-209`) | **S pero CRÍTICO** | Money-closed: un crédito castigado no puede volver a un pay run. Tests obligatorios |
| Comando + handler de cierre, transaccional (asiento + créditos + auditoría en un `SaveChanges`) | M | El patrón transaccional existe; la novedad es que toca dos agregados |
| Concurrencia (§5.2) | S | Ninguna de las dos opciones necesita migración |
| Auditoría con ids de crédito | **XS** | La infraestructura ya está y el handler ya la usa |
| Permiso propio de cierre | XS | Una constante + una línea en el mapa de roles |
| Ceremonia de confirmación al nivel de `Mark as paid` | S | Hay un modal de referencia para copiar |
| **Estado intermedio (§6.3)** | **M, y probablemente WI aparte** | Toca `PayeeStatus` o una entidad nueva, y cambia la definición de la cola |

**Lectura:** el lado del **ledger** está listo (tipos, append-only, auditoría, patrón transaccional). El
lado de los **créditos** no existe y arrastra migración. **Recomiendo partirlo en dos:** primero el
cierre del **balance** —que hoy ya se puede hacer y sólo necesita ceremonia, permiso propio y auditoría
enriquecida, sin una sola migración—, y después el write-off de **créditos**, que es el WI con esquema.
El estado intermedio va tercero o aparte: sin él, la única salida para un litigio es un asiento falso,
así que **no debería quedar para "algún día"**.

---

**Sin cambios de producto. Sin escrituras. Sin migraciones.** — para revisión de Rodolfo.
