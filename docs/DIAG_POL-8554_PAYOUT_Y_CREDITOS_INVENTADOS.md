# Diagnóstico READ-ONLY — POL-8554 no genera payout, y el asistente cruzó las filas de dos créditos

**Fecha:** 2026-08-28 · **Rama:** AI-CHAT-ASSISTANT · **Sin commit** · **Sin cambios de producto**
**Alcance:** dos defectos independientes. Nada se arregló; todo lo que sigue es medición o está marcado
como no establecido.

**Fuentes:** código en `WasnieApi/src` y `WasnieUi/src`; base de dev `HEYBALDUR / WasnieDb` (sólo
`SELECT`); logs en `WasnieApi/src/Wasnie.Api/logs/wasnie-20260827.log`; la conversación del asistente
`cf5762b9-de40-48c8-8388-69b5d86fa61e` leída de `AssistantMessages`.

---

## ★ Dos premisas del WI que los datos contradicen

Antes de nada, porque cambian el diagnóstico:

1. **La payee SÍ está asignada al plan del crédito.** El WI dice que su única asignación es *Q3 2026 —
   Plan Comercial EMEA*. En la base hay **dos** asignaciones activas, y una es exactamente al plan del
   crédito. La falta de asignación **no** es el motivo, y la pantalla que mostró una sola merece su
   propia mirada (§A.6).

2. **El crédito de €5.999 y la fecha 24-jun-2026 son REALES.** El asistente no los inventó: están en la
   base y la herramienta se los entregó. Lo que inventó es **de quién es cada uno** — cruzó las dos
   filas. Es un error distinto y, en un sentido, peor de diagnosticar (§B.3).

---

# FRENTE A — Por qué el run no genera payout

## A.1 La consulta que selecciona créditos elegibles

El pay run **no tiene consulta propia**: `CalculatePayRunHandler` crea/reutiliza el `PayRun` y delega el
cálculo en `CalculatePayoutsForPeriodCommand`
(`CalculatePayRunHandler.cs:94-96`). Toda la elegibilidad vive en
`WasnieApi/src/Wasnie.Application/Compensation/Handlers/Payouts/CalculatePayoutsForPeriodHandler.cs`.

**★ El punto de partida no son los créditos, son las ASIGNACIONES.** El motor nunca enumera créditos del
tenant y luego los filtra: itera asignaciones y, para cada una, va a buscar créditos. Un crédito cuya
asignación no entra en el bucle **nunca es mirado**. Esto explica por qué el mensaje de error habla de
créditos y el motor en realidad nunca llegó a preguntarse por ellos.

Los filtros, en el orden en que se aplican:

| # | Filtro | file:line |
|---|---|---|
| G1 | `PlanAssignments.TenantId == tenant` **y** `Status == Active` | `:39-42` |
| — | filtro opcional por payee (el pay run no lo usa) | `:45-48` |
| G2 | la asignación **solapa** el período: `EffectivePeriod.Start <= PeriodEnd && EffectivePeriod.End >= PeriodStart` | `:51-55` |
| — | corte temprano: sin asignaciones solapadas → `0 payouts`, sin conflictos ni avisos | `:57-59` |
| **G3** | **se descartan las asignaciones de payees `Terminated`** | `:75-96` |
| — | corte temprano: si no queda ninguna → `0 payouts`, sin conflictos ni avisos | `:93-95` |
| G4 | el plan existe y **no** está `Archived` (si no, `continue` mudo) | `:102-108`, `:129` |
| G5 | intersección `[max(inicio), min(fin)]` no vacía | `:118-125` |
| **G6** | **conflicto**: ya existe un payout `Approved`/`Paid` para ese `(payee, plan, período exacto)` → se registra el conflicto y se salta | `:138-165` |
| — | un payout `Calculated` del mismo período se BORRA y se rehace | `:167-181` |
| G7 | transacciones: `PayeeId`, **`TransactionDate` dentro de la intersección**, `Amount.Currency == plan.Currency` | `:186-194` |
| G8 | créditos: `PayeeId`, **`PlanId == assignment.PlanId`**, `SupersededAt == null`, `ConsumedAt == null`, `TransactionId ∈` los de G7 | `:201-209` |

## A.2 ¿Excluye payees terminados? — Sí, y antes de todo lo demás

**Medido.** `CalculatePayoutsForPeriodHandler.cs:75-96`. El estado del payee **sí** entra en la
selección, y entra en el peor lugar posible para el diagnóstico: **antes** de la detección de conflictos
y antes de mirar un solo crédito.

Un crédito activo de una persona `Terminated` **no se descarta — nunca se evalúa**. Su asignación
desaparece de la lista y el bucle ni siquiera la visita.

El comentario del código es explícito y el diseño es deliberado (`:61-74`): un terminado no genera
payouts nuevos, la deuda se congela sin borrarse, y una persona de finanzas cierra la cuenta. La
exclusión es **correcta**. Lo que no es correcto es que sea **muda**: la única huella es
`logger.LogInformation("CalculatePayouts: skipped {N} assignment(s) of {M} terminated payee(s).")` en el
servidor. **El resultado que viaja al usuario —`CalculatePayoutsResult(PayoutsCreated, Conflicts,
Warnings)`— no lleva ni un campo para esto.**

**Interacción con el subsistema de cuentas huérfanas — ★ y acá hay un agujero real.**
`ListTerminatedPayeesWithBalanceHandler.cs:54-68` construye la cola de finanzas cruzando
`PayeeBalances` con `Payees.Status == Terminated` **y `Balance.Amount != 0`**.

```
PayeeBalances WHERE PayeeId = Birgit  →  0 filas
```

Y `PayeeBalance.Apply` sólo se invoca desde tres lugares —`PayRunSettlementService.cs:117`,
`CreateManualLedgerAdjustmentHandler.cs:81`, `RegisterDealChurnClawbackHandler.cs:184`—: liquidación de
un pay run, ajuste manual y clawback. **Un crédito sin consumir no es un evento de ledger y no crea
balance.**

> **★ El resultado es un punto ciego doble.** El motor la salta por terminada, y la red de seguridad que
> existe precisamente para las terminadas no la ve porque mira balances, no créditos. Los **€3.869,34**
> no están en ningún payout, no están en la cola de cuentas huérfanas, y no están en la tarjeta
> "Terminated Accounts" del dashboard (misma fuente). No hay ninguna pantalla en Incentra donde este
> dinero aparezca como pendiente.

## A.3 ¿Exige asignación vigente al plan del crédito? — Sí, pero **acá pasa**

**Medido.** Se exige en dos puntos: la asignación debe solapar el período (`:51-55`) y el crédito debe
ser del **mismo plan** que la asignación (`:205`).

Contra la premisa del WI, la payee tiene **dos** asignaciones activas:

| Assignment | Plan | Vigencia | Estado | Creada |
|---|---|---|---|---|
| `B57810A0` | **EU Accelerator Q2 2026** ← el plan del crédito | 2026-04-01 → 2026-06-30 | Active | 2026-06-17 |
| `A2E39C9F` | Q3 2026 — Plan Comercial EMEA (Test Integral) | 2026-07-01 → 2026-09-30 | Active | 2026-08-27 15:15:49 |

La asignación al plan del crédito **existe, está activa y cubre junio**. G1, G2 y G8 pasan. **Las fechas
tampoco eran el problema, pero no por la razón que dice el WI.**

## A.4 El período se filtra contra `TransactionDate`, no contra `AllocatedAt`

**Medido.** `:190-191` — `t.TransactionDate >= intersectionStart && t.TransactionDate <= intersectionEnd`.

**`Credit.AllocatedAt` no aparece en ningún filtro del motor de cálculo.** No se usa para nada en la
selección.

Valores reales de este caso:

| Campo | Valor |
|---|---|
| `CompensationTransactions.TransactionDate` | **2026-06-16** ← el campo que el motor mira |
| `CompensationTransactions.IngestedAt` | 2026-06-22 |
| `Credits.AllocatedAt` (crédito activo) | 2026-08-27 15:17:37 UTC ← **el motor NO lo mira** |

> **Toda la teoría del asistente —"el run busca créditos cuya fecha de asignación caiga en el período"—
> es falsa sobre el producto.** No es una fecha equivocada: es un mecanismo que no existe. Y sobre esa
> teoría construyó las dos recomendaciones (junio y luego agosto), las dos inútiles.

## A.5 La consulta ejecutada a mano — cuál filtro descarta el crédito

Reproduje la cadena en SQL sobre `WasnieDb`, puerta por puerta, para tres rangos:

```
Label    | G1_assignment | G2_not_terminated | G3_plan_not_archived | G5_tx_in_intersection | G6_credit_row | G7_no_blocking_payout
---------|---------------|-------------------|----------------------|-----------------------|---------------|----------------------
june     | PASS          | EXCLUDES          | PASS                 | PASS                  | PASS          | BLOCKS (conflict)
august   | EXCLUDES      | EXCLUDES          | PASS                 | EXCLUDES              | PASS          | PASS
all-time | PASS          | EXCLUDES          | PASS                 | PASS                  | PASS          | BLOCKS (conflict)
```

**Junio tiene DOS bloqueos independientes y suficientes por sí solos:**

1. **`Payees.Status = 2 (Terminated)`**, `TerminationDate = 2026-08-27`. Corta en `:75-96`, antes que
   nada.
2. **Ya existe un payout `Paid` para exactamente `2026-06-01 → 2026-06-30`** de ese payee y ese plan
   (`7149D09C`, pay run `D4B34638`). Si la terminación no existiera, esto entraría en G6 como conflicto.

*(Todavía más notorio: ese payout `Paid` de junio tiene **cero líneas**. Las únicas tres
`PayoutLines` de la payee cuelgan del payout `2026-04-01 → 2026-06-30`, y ninguna referencia este
crédito.)*

**Agosto** no podía funcionar de ninguna manera: la asignación al plan del crédito termina el 30-jun, y
la transacción es del 16-jun. El consejo del turno 7 estaba condenado antes de escribirse.

**All-time** replica junio: la intersección es 2026-04-01 → 2026-06-30, para la que existe el payout
`Paid` `D6E62E22`.

**El orden importa.** Como la terminación corta en `:75-96` y el conflicto se detecta en `:150-165`, el
conflicto de esta payee **ni siquiera llega a reportarse**. El usuario no ve la terminación (no hay
campo) y tampoco ve el conflicto (nunca se generó).

### ★ PUERTA DE PARADA — el mensaje al usuario es falso

`WasnieUi/.../pay-runs-list.component.html:246-253`:

```html
@if (result.payoutsCreated > 0) { …CALCULATE_CREATED… }
@else { {{ 'PAY_RUNS.CALCULATE_NO_PAYOUTS' | translate }} }
```

`en.json:1650` → *"No payouts created. No matching credits found for this period."*

**Es una causa afirmada por el cliente que el backend nunca estableció.** El backend devuelve un contador
en cero y nada más; la UI le pone un porqué. En este run concreto, medido a nivel tenant para junio:

| | |
|---|---|
| Asignaciones activas que solapan junio | **24** |
| …de payees `Terminated` → **descartadas en silencio** | **4** |
| Supervivientes | 20 |
| …de esos 20, con payout `Approved`/`Paid` previo → **conflicto** | **20 (el 100%)** |

De modo que la frase es falsa **dos veces**: había créditos de sobra, y ni la terminación ni los 20
conflictos tienen nada que ver con "este período". **Defecto de producto. No arreglado acá** (§C.1).

## A.6 Estado inconsistente del dato — reportado, no tocado

**No se modificó ni una fila.** Lo que hay que mirar, en orden de aparición el 27-ago-2026:

| Hora (UTC) | Qué pasó |
|---|---|
| 15:14:57 | El crédito de €5.999 de **Adrian Dominguez** se marca `SupersededAt` |
| 15:15:49 | Se crea la asignación a *Q3 2026 — Plan Comercial EMEA* para Birgit |
| **15:16:41** | **Birgit Schneider pasa a `Terminated`** (`UpdatedAt`) |
| **15:17:37** | **Se crea el crédito de €3.869,34 a nombre de Birgit** (`AllocatedAt`) |

**★ El crédito se generó 56 segundos DESPUÉS de terminarla.** El motor de créditos aceptó atribuir
comisión a una persona que el motor de payouts ya no procesa. No establecí si eso es un camino
soportado (una reatribución legítima post-terminación) o una guarda faltante — hace falta revisar
`CreditAllocationService`, que queda fuera del alcance de este WI. Lo reporto como **inconsistencia a
decidir**, no como defecto confirmado.

**Un crédito activo de €3.869,34 de una persona terminada no se corrige a mano.** Queda como está.

**Y una segunda cosa para mirar:** el WI reporta que la pantalla muestra **una sola** asignación cuando
la base tiene **dos** activas. No investigué la pantalla de asignaciones (fuera de alcance). Si se
confirma, es un tercer defecto: la evidencia sobre la que Rodolfo y el asistente razonaron era
incompleta.

---

# FRENTE B — De dónde salieron las fechas

## B.1 ★ El payload literal NO existe en los logs — y por qué

`AssistantToolRunner.cs:172` registra **sólo el nombre de la herramienta**:

```
"The assistant ran the read-only tool {Tool}."
```

Y el comentario de arriba (`:169-171`) dice que los **argumentos** se omiten a propósito: *"they are the
user's own words about their own records, and a log is not where those belong."* **El resultado tampoco
se registra.** `GetTransactionTool.LogCause` (`:200-201`) añade un `Found`/`NotFound` y explícitamente
no registra ni la referencia.

> **El payload literal que pide el WI no se puede recuperar de los logs. Es una decisión de privacidad
> deliberada, no una falla.** Lo reconstruí por otra vía: leyendo la proyección de la herramienta y
> ejecutando contra la misma base las mismas consultas que ejecutó ese día. La reconstrucción es
> **derivada**, no una copia literal — la marco como tal.

## B.2 Qué corrió, y en qué turno

De `wasnie-20260827.log`, conversación `cf5762b9-…` (horas locales, +02:00):

| Turno | Hora | Herramienta | Causa |
|---|---|---|---|
| 1 (seq 1) | 17:19:48/49 | `get_transaction` | **Found** |
| **3 (seq 3)** | 17:21:30 | **NINGUNA** | — |
| 5 (seq 5) | 17:22:34 | `get_transaction` | **Found** |
| 7 (seq 7) | 17:26:45/46 | `get_transaction` | **Found** |

## B.3 ★★ Las cifras vinieron de la herramienta. El CRUCE lo hizo el modelo.

`GetTransactionTool.ReadCommissionAsync` (`:234-277`) pide `ListCreditsQuery` con
`Reference = "POL-8554"`, **`Status = "All"`**, `PageSize = 25`. Devuelve **todos** los créditos de la
transacción, superseded incluidos. `ListCreditsHandler.cs:28-35`: orden por defecto **`AllocatedAt`
DESC**.

**Lo que hay en la base** (`Credits WHERE TransactionId = 043B6CBE…`), en el orden exacto en que la
herramienta lo entregó:

| # | `commissionAmount` | `creditAllocatedAt` | `creditIsSuperseded` | Payee real |
|---|---|---|---|---|
| **[0]** | **3.869,3432 EUR** | **2026-08-27** | **false** | Birgit Schneider (DE-101) |
| **[1]** | **5.999,0000 EUR** | **2026-06-24** | **true** | **Adrian Dominguez (NB-2001)** |

**Lo que el asistente escribió en el turno 5:**

| # | Su afirmación |
|---|---|
| "primer crédito" | `creditAllocatedAt` **2026-06-24** → la comisión de **3.869,34** |
| "segundo crédito, **supersedido**" | `creditAllocatedAt` **2026-08-27** → **5.999,00**, marcado superseded |

**Veredicto: ninguna cifra es inventada. Las cuatro son reales. La ASOCIACIÓN entre ellas es fabricada.**

El mecanismo es preciso y verificable: tomó los **importes** en el orden del array (3.869,34 primero,
5.999 después) y las **fechas en el orden inverso**, y colgó el flag `superseded` de la segunda fila.
Barajó una tabla de dos filas. Cada valor por separado sobrevive a una verificación; el par no.

**Esto NO es la tercera alucinación aritmética del día.** Es una clase distinta: **mala atribución entre
filas de un payload correcto**. Importa porque las defensas son distintas — ninguna regla contra
"derivar cifras" habría atrapado esto, porque no derivó nada.

**Sí hubo una fabricación pura, y está en el turno 3:** *"la fecha de comisión 28-Jun-2026"*. **Ese
turno no corrió ninguna herramienta.** Ninguno de los dos créditos es del 28-jun. Salió de la nada.

**Y hay una tercera categoría, la peor de las tres:** el **mecanismo** inventado — *"los payouts sólo
aparecen cuando el período del Pay Run incluye la fecha en la que el crédito quedó asignado"*. Es una
regla de producto que no existe (§A.4), afirmada con total confianza en los turnos 5 y 7, y es la que
generó las dos instrucciones que le hicieron perder el tiempo a Rodolfo.

**Lo que el asistente sí reportó bien** (para no exagerar el defecto): `hasBeenPaid: false` y la frase
*"The commission has been credited but does not yet appear in any payout"* son **literales** del
payload — `SettlementResult.NotYetPaid`, `GetTransactionTool.cs:424-426`. Y "dos créditos, 3.869,34 y
5.999,00" del turno 1 es **correcto**.

## B.4 ★ Un hueco real en la proyección que contribuyó

`CommissionEarned` (`GetTransactionTool.cs:395-404`) marca `PayeeId` con `[property: JsonIgnore]`.

> **La herramienta entregó dos créditos de DOS PAYEES DISTINTOS sin un solo campo que los distinga.**
> Desde el payload, los €5.999 de Adrian Dominguez y los €3.869,34 de Birgit Schneider son
> indistinguibles como "dos créditos de esta transacción". El modelo no tenía cómo saber que el
> superseded era de otra persona — y esa es justamente la información que explica todo el caso: la
> transacción fue **reatribuida** de Adrian a Birgit.

No excusa el cruce de filas (el pairing correcto sí estaba disponible), pero es un defecto de proyección
por derecho propio.

**Y un segundo efecto, latente:** `ReadSettlementAsync` (`:290-294`) usa `commission.Credits[0].PayeeId`
y `.PlanId` para buscar payouts. Con dos payees en una transacción, sólo inspecciona los del primero —
que depende del orden `AllocatedAt DESC`. No causó daño acá, pero es frágil.

## B.5 Qué regla existe y por qué no aplicó

**Sobre reportar el payload tal cual — la regla existe (10f):**

> *"Report the amounts and the steps AS RETURNED, **in the order returned**. Do not re-order them into a
> more logical sequence…"* — `AssistantPrompt.cs:558-560`

Es exactamente la regla que el turno 5 violó. Y **sí viajó**: en ese turno corrió una herramienta.
Presente y desobedecida.

**Y acá está la parte que el WI predijo bien — `DataRules` sólo viaja cuando corrió una consulta.**
`AssistantPrompt.BuildSystemMessage` (`:947-950`):

```csharp
var hasData = !string.IsNullOrWhiteSpace(toolData);
var dataBlock = hasData ? $"\n{DataRules}\n\n{DataHeader}…" : string.Empty;
```

**El turno 3 no corrió herramienta → `DataRules` completo ausente.** Ni 10d (no hacer aritmética), ni
10e (no recomputar), ni 10f (no reordenar), ni 10c. Lo único vigente era `ConfinementRules`, cuya única
regla numérica (`NumericRule`, rule 6, `:345-360`) es sobre **formatos de campos numéricos**, no sobre
afirmar hechos de un registro sin tenerlo delante.

Y `AssistantPrompt.Build` (`:1053-1055`) pasa al modelo **sólo el `Content`** de los mensajes previos —
los payloads no persisten en el historial. **En el turno 3 el modelo no tenía datos, no tenía las reglas
de datos, y afirmó una fecha de dinero igual.** Es el cuarto mordisco del mismo diseño.

## B.6 ★ El cambio de versión sin admitirlo — la regla existe, y es 10c

El WI supone que admitir el error propio no está cubierto. **Sí lo está, y en la misma regla que cubre
aceptar la observación.** `AssistantPrompt.cs:535-543`:

> *"10c. ★★ WHEN THE USER SAYS THEY LOOKED AND IT IS NOT THERE, THEY ARE RIGHT AND YOU ARE WRONG. […]
> **Do NOT repeat the steps you already gave; they did not work. Say plainly that your explanation was
> wrong**, then either name a different source you can actually see in the data, or say you cannot
> determine it with what you can look up and where they should look. Never insist on an explanation
> about MONEY that the user has just told you is false."*

**El turno 7 corrió una herramienta, así que 10c viajó.** Y lo violó en sus dos mitades:

1. **No dijo que se había equivocado.** Pasó de junio a agosto como si siempre hubiera dicho agosto.
2. **Repitió los mismos pasos.** Los seis pasos del turno 7 (Remove Draft → Calculate → rango → Recalculate
   credits → Approve → Mark as paid) son los mismos del turno 5, con las fechas cambiadas. Es literalmente
   lo que la primera frase de 10c prohíbe.

Peor: cerró sugiriendo *"verifica que Birgit Schneider esté asignada al plan EU Accelerator Q2 2026"* —
**la única pista correcta de toda la conversación, y la puso al final como duda, después de afirmar que
con el rango correcto "debería incluirse automáticamente".**

**No es un hueco de reglas. Es una regla presente, en contexto, desobedecida.** El WI proponía escribir
una regla nueva; los datos dicen que escribir otra regla no es la respuesta.

---

# Medido vs. supuesto vs. no establecido

## Medido
- El motor arranca por asignaciones, no por créditos (`CalculatePayoutsForPeriodHandler.cs:39-55`).
- La terminación excluye antes que nada y **no viaja al usuario** (`:75-96`; `CalculatePayoutsResult` sin campo).
- El período filtra `TransactionDate`; `AllocatedAt` no se usa jamás en el cálculo (`:190-191`).
- Birgit está `Terminated` (`Status=2`, 2026-08-27) y **sí** tiene asignación activa a EU Accelerator Q2.
- Junio: dos bloqueos suficientes (terminación + payout `Paid` preexistente para el período exacto).
- Junio a nivel tenant: 24 asignaciones → 4 descartadas en silencio → 20 supervivientes → **20/20 en conflicto**.
- `PayeeBalances` de Birgit: **0 filas** → invisible en la cola de cuentas huérfanas.
- El mensaje "No matching credits found for this period" es un literal de cliente disparado por `payoutsCreated === 0`.
- Los logs **no** contienen payloads de herramientas, por diseño.
- 3 llamadas a `get_transaction` (turnos 1, 5, 7), todas `Found`; **el turno 3 no llamó a nada**.
- Los dos créditos, sus dos importes y sus dos fechas son reales; el orden entregado fue `AllocatedAt DESC`.
- `DataRules` sólo viaja con `toolData` no vacío (`:947-950`); el historial lleva sólo `Content` (`:1053-1055`).
- Las reglas 10c y 10f existían y viajaron en los turnos que las violaron.

## Supuesto (razonado, no ejecutado)
- El payload reconstruido en §B.3 es **derivado** de leer la proyección + reejecutar las consultas hoy.
  Coincide en los cuatro valores y en el orden, y las dos frases literales que el asistente citó
  (`hasBeenPaid`, la nota de settlement) salen exactas — pero no es una copia capturada del día.
- El cruce de filas como "importes en orden del array, fechas en orden inverso" explica los datos
  observados; es la lectura más simple, no una traza del modelo.

## No establecido
- **Por qué** se creó un crédito 56 s después de terminar a la payee. No revisé `CreditAllocationService`.
- Si la pantalla de asignaciones realmente muestra una sola de las dos (reportado por el WI, no verificado).
- De dónde salió el "28-Jun-2026" del turno 3. Sin herramienta y sin payload en historial, no hay traza.
- Si los 20 conflictos de junio se le mostraron a Rodolfo. La UI tiene sección para conflictos; no verifiqué
  qué vio en pantalla.
- Si esta reatribución Adrian → Birgit es un flujo soportado o un accidente de datos de prueba.

---

# C. Recomendaciones — NINGUNA implementada

## C.1 El mensaje que miente (mayor, y el más barato)
`CalculatePayoutsResult` debería llevar por qué no se creó nada: al menos `skippedTerminated` y el conteo
de conflictos, y la UI dejar de afirmar una causa que no conoce. **Mientras tanto, la frase debería decir
menos, no más:** "No se creó ningún payout" es verdad; el resto no. Es un cambio de una clave de i18n y
resuelve la mitad del daño.

## C.2 El punto ciego doble de la terminación (mayor — money-closed)
Un crédito activo, no consumido, de un payee terminado no aparece en ninguna pantalla. La cola de cuentas
huérfanas mira `PayeeBalances`, que los créditos no alimentan. **Decisión de producto, no un parche:**
o la cola pasa a mirar también créditos sin consumir, o el flujo de terminación exige resolverlos.
**Requiere una decisión de Rodolfo antes de tocar nada.**

## C.3 El payload que no distingue payees (medio)
Exponer el payee por crédito en `CommissionEarned` — el nombre, no el id (regla 10b). Un caso de
reatribución es inexplicable sin él, y `ReadSettlementAsync` deja de depender de que `Credits[0]` sea el
payee "correcto".

## C.4 `DataRules` ausente sin herramienta (mayor — es el cuarto caso)
El turno 3 es el mismo diseño mordiendo por cuarta vez. La parte de `DataRules` que **no** habla del
payload —no afirmar hechos de un registro que no tenés, no inventar fechas de dinero— no depende de que
haya datos y no debería viajar con ellos. Candidata a mudarse a `ConfinementRules`. **Con presupuesto de
tokens medido antes**, que ya está justo.

## C.5 Reglas presentes y desobedecidas (10c, 10f)
**Recomiendo explícitamente NO escribir reglas nuevas para esto.** Estaban ahí, viajaron, y no se
cumplieron. Añadir una sexta redacción del mismo mandato diluye el prompt y consume el presupuesto que
C.4 necesita. Si esto se persigue, el camino es medir cumplimiento, no redactar más.

## C.6 El crédito post-terminación (a decidir, no a tocar)
Establecer si crear un crédito para un payee `Terminated` es soportado. Si no lo es, la guarda va en la
asignación de créditos, no en el cálculo de payouts. **El dato existente se deja como está.**

---

**Sin cambios de producto. Sin escrituras a la base. Sin commit.** — para revisión de Rodolfo.
