# WI 3/3 — Diagnóstico y PUERTA DE PARADA: el payload de créditos no dice de quién es cada fila

**Fecha:** 2026-08-28 · **Rama:** AI-CHAT-ASSISTANT · **Sin commit** · **NADA CONSTRUIDO**

> **★★ ESTE WI ESTÁ DETENIDO EN SU PROPIA PUERTA DE PARADA (§2), Y POR DOS MOTIVOS INDEPENDIENTES.**
> Ninguno de los dos es una opinión mía: el primero está escrito en `docs/Legal.md` como mitigación **ya
> decidida**, y el segundo está medido. La decisión es de Rodolfo.

---

# PASO 3 — Diagnóstico read-only

## 3.1 La proyección actual, y por qué el `[JsonIgnore]` es deliberado

`WasnieApi/src/Wasnie.Application/Assistant/Tools/GetTransactionTool.cs:395-404`:

```csharp
private sealed record CommissionEarned(
    decimal CommissionAmount,
    string CommissionCurrency,
    string MatchedPlanName,
    string MatchedRuleName,
    bool CreditIsSuperseded,
    string CreditAllocatedAt,
    [property: JsonIgnore] Guid CreditId,
    [property: JsonIgnore] Guid PayeeId,
    [property: JsonIgnore] Guid PlanId);
```

**Seis campos viajan. Los tres ids NO**, y el motivo está escrito en el propio archivo (`:363-364`):
*"Ids are included only where they are needed to join the walk together; nothing here is a secret, but
nothing here is a screen the assistant can link to either."* Los tres existen sólo para que
`ReadSettlementAsync` pueda caminar del crédito al payout **dentro de C#**.

**★ El `[JsonIgnore]` es CORRECTO y no hay que quitarlo.** La regla 10b del prompt
(`AssistantPrompt.cs:531-535`) dice: *"NEVER put an id in your answer, not in brackets, not as a
reference"*. Exponer el GUID metería en el contexto del modelo justo lo que la regla le prohíbe
imprimir, y confiaría en que se contenga. **La solución no es des-ignorar el id: es agregar una
etiqueta que sí se pueda leer** — y cuál sea esa etiqueta es exactamente la decisión del §2.

**Lo que el payload NO tiene:** ni un solo campo que distinga la fila 0 de la fila 1 salvo sus propios
importes y fechas. Cuando el modelo tiene que decir "el primer crédito", no tiene a qué agarrarse.

## 3.2 ¿Puede haber filas de más de un payee? Sí — y está medido

`ReadCommissionAsync` (`:239-247`) llama a `ListCreditsQuery` con **`Reference` y nada más** —
`Status = "All"`, `PageSize = 25`. El filtro es la **transacción**, no la persona. Todo crédito de esa
transacción entra, de quien sea.

Escenarios que lo producen: **reatribución** (la transacción se reasigna, el crédito viejo queda
superseded y nace uno nuevo a nombre de otro), y **splits** (varios payees sobre una venta).

Medido sobre la base de dev:

| | |
|---|---|
| transacciones con créditos | 591 |
| **cuyas filas abarcan MÁS DE UN payee** | **1** ← POL-8554 |
| máximo de créditos en una transacción | **7** |
| máximo de payees distintos en una transacción | **2** |

Es raro. También es exactamente el caso que rompió, y el que un producto de comisiones va a ver cada
vez que alguien corrige una atribución.

**★ Nota de visibilidad, fuera de alcance pero relevante para elegir:** `ListCreditsHandler` exige
`Credits.Read` y **no aplica `PayeeAccessGuard`** (`ListCreditsHandler.cs:22`). No hay filtro por payee
visible. Hoy no se nota porque el chat es admin-only (`AssistantEntitlement.HasSeat()`), pero significa
que la alternativa B haría viajar el nombre de un tercero por una consulta que no tiene control de
visibilidad por persona propio.

## 3.3 ¿Otras herramientas con el mismo defecto? — No. Es la única.

Revisé todas las colecciones de todos los payloads del asistente:

| herramienta | colección | ¿fila autoidentificada? |
|---|---|---|
| `get_payee_plans` | `Assignments` | ✅ `PlanName` + `PlanVersion`; y todas son del MISMO payee, nombrado arriba |
| `get_payee_balance` | `Balances` | ✅ `Currency` es la clave; un solo payee |
| `get_plan_rules` | `Rules`, `OtherVersionsOfThisPlan`, `AvailablePlans` | ✅ `RuleName`+`SortOrder`, `Version`+`Status`, `Name`+`Version` |
| `simulate_plan_rules` | `Rules`, `Steps` | ✅ `RuleName`+`SortOrder`, `Component` |
| **`get_transaction`** | **`Commissions`** | ❌ **nada distingue una fila de otra** |

**El defecto no es del patrón: es de esta herramienta.** Las demás llevan una clave intrínseca porque su
colección es de cosas nombrables. `Commissions` es la única colección de filas cuyo distintivo es una
PERSONA, y la persona es justo lo que se ocultó.

**★ Y ya existe la casa para el requisito §4.2 (orden explícito):** `get_plan_rules` manda
`CalculationOrder: ["RateTable","Modifier","Cap","Floor"]` con este comentario (`:431-435`): *"THE ORDER
IS DATA, because the assistant got it wrong by inference"*. Mismo problema, misma cura, ya probada.

## 3.4 El coste en tokens, medido

Heurística `chars / 4`, la misma del guard. Presupuesto de referencia, todo previamente medido y
registrado (`docs/PROJECT_STATUS.md`, 2026-08-27): techo **24.000**; request ensamblado del guard
**21.021** (incluye un `ToolData` de balance de 114 tok); esquemas que el guard nunca midió **1.904**.
**Sólo corre UNA herramienta por turno**, así que un turno de `get_transaction` y uno de simulación son
requests distintos y no se suman.

**El caso real (POL-8554, 2 créditos):**

| variante | payload | request | margen |
|---|---|---|---|
| hoy | 233 | 23.044 | **+956 (4,0 %)** |
| A: `payeeRef` opaco + orden explícito | 255 | 23.066 | +934 (3,9 %) |
| B: nombre + código + orden explícito | 275 | 23.086 | +914 (3,8 %) |

**El caso peor que EXISTE en la base (7 créditos):**

| variante | request | margen |
|---|---|---|
| hoy | 23.331 | **+669 (2,8 %)** |
| A | 23.380 | +620 (2,6 %) |
| B | 23.461 | **+539 (2,2 %)** |

**★★ Y el caso peor que la herramienta PERMITE (`PageSize = 25`), que ya está roto hoy:**

| créditos | hoy | A | B |
|---|---|---|---|
| 15 | +211 | +118 | **−53 ✗** |
| 18 | +39 | **−70 ✗** | −275 ✗ |
| 19 | **−133 ✗** | −258 ✗ | −497 ✗ |
| 25 | **−362 ✗** | −509 ✗ | −793 ✗ |

**El techo se rompe con 19 créditos SIN tocar nada.** No lo causa este WI; este WI lo adelanta a 17 (A)
o a 15 (B). Nada en dev llega a 8, así que hoy es teórico — pero el `PageSize = 25` de la propia
herramienta lo permite, y un tenant con muchos splits llegaría.

---

# ★★ PUERTA DE PARADA — dos condiciones disparadas

## Condición 1 (§2) — la seudonimización YA está decidida, y dice lo contrario

`docs/Legal.md §3.2`, **Prioridad 1, mitigación DECIDIDA**, titulada:

> **"PII ESTRUCTURAL — `payeeName` + `payeeEmployeeCode` salen en cada lookup"**
>
> *"Cada vez que el asistente consulta una transacción, el **nombre real del empleado** y su **código de
> empleado** viajan al proveedor — **por diseño, no por accidente**."*
>
> **Mitigación decidida — hidratación cliente-side:**
> 1. El backend envía al modelo un **`payeeId` opaco** en lugar del nombre.
> 2. Angular re-sustituye el nombre desde su caché local antes de renderizar.
> 3. El dato personal **nunca sale a la red del LLM**.

**La alternativa B es literalmente lo contrario de la mitigación ya decidida**, sobre los dos campos que
esa entrada nombra, y en el endpoint que esa entrada usa como ejemplo. Y se agrava: hoy viaja **un**
nombre (el payee de la transacción, que el usuario ya ve en pantalla); la B haría viajar **los nombres
de terceros** — gente que no es el sujeto de la conversación, por una consulta sin filtro de visibilidad
por payee (§3.2).

Contexto que empeora la decisión, no que la salve: **§3.1 DPA AUSENTE — RELEASE BLOCKER**, y
`docs/Legal.md` es explícito en que las mitigaciones técnicas *"reducen el riesgo técnico; **no crean la
base jurídica**"*.

**★ Esto es lo que el WI §2 previó: "Si el plan de seudonimización contempla no mandar nombres de
payees, este WI lo contradice y hay que parar y reportar." Lo contempla. Paro.**

## Condición 2 (§4.4) — el margen ya está por debajo del 2 % en el peor caso real

El WI dice: *"Si el margen cae por debajo del 2 %, parar y reportar. No subir el techo, no recortar el
manual."* El peor caso **vinculante** del sistema sigue siendo el turno de simulación: **23.521 / 24.000
→ 479 (2,0 %)**, y a 6 rules ya se pasa. Este WI no lo mueve. Pero en la ruta de `get_transaction` el
techo **ya se rompe con 19 créditos hoy**, y las dos alternativas lo adelantan.

**No subí el techo, no recorté el manual, no comprimí reglas.**

---

# Las dos alternativas, con su implicancia

| | **A — identificador opaco** | **B — nombre + código** |
|---|---|---|
| Ejemplo de fila | `"payeeRef": "payee-2"` | `"payeeName": "Adrian Dominguez", "payeeEmployeeCode": "NB-2001"` |
| ¿Arregla el barajado? | **Sí, completo.** El modelo ya no puede fundir dos filas | **Sí, completo** |
| ¿Puede decir de quién es? | **No.** Como mucho *"un crédito de otro payee"* | **Sí:** *"ese crédito de €5.999 es de Adrian Domínguez, quedó supersedido al reasignar la transacción"* — la respuesta que Rodolfo necesitaba desde el primer turno |
| Datos personales nuevos al proveedor | **Ninguno** | **Nombres y códigos de TERCEROS**, en cada lookup con más de un payee |
| Contra `Legal.md §3.2` | **Alineada** — es la misma idea (id opaco) que la mitigación decidida | **Contradice la mitigación decidida** |
| Margen a 7 créditos | +620 (2,6 %) | +539 (2,2 %) |
| Rompe el techo a partir de | 17 créditos | 15 créditos |

**Ninguna de las dos es mía para elegir.** La A es técnicamente suficiente para el defecto reportado (el
barajado) y no abre frente legal. La B es la única que da la respuesta útil, y cuesta exactamente lo
que `Legal.md §3.2` decidió dejar de pagar.

**Un matiz que conviene tener a mano al decidir:** el nombre del payee **de la transacción** ya viaja
hoy (`TransactionLifecycle.PayeeName`), así que la A no deja al asistente mudo — puede decir *"hay otro
crédito sobre esta transacción, de otra persona, supersedido"*, que ya habría evitado el error
completo. Lo que la A no puede es **nombrar** a esa persona.

---

# Lo que NO hice, y por qué

- **Nada construido.** Ni el campo, ni el orden explícito, ni tests del cambio.
- **No escribí ninguna regla nueva sobre admitir el error propio.** El diagnóstico ya estableció que la
  10c lo cubre, que viajó, y que se violó igual; otra redacción del mismo mandato gasta presupuesto sin
  ganar nada. (Fuera de alcance por el propio WI, y coincido.)
- **No toqué la seudonimización ni el DLP** — frente propio, `Legal.md` §3.2 y §3.3.
- **No toqué `ListCreditsHandler`** pese a la ausencia de `PayeeAccessGuard`: fuera de alcance,
  reportado arriba.
- **No subí el techo de tokens.**

---

# Recomendación (una sentencia, y la decisión sigue siendo de Rodolfo)

**Alternativa A ahora, B nunca sin el DPA.** La A cierra el defecto reportado —el barajado— por completo,
no agrega un solo dato personal, y es la misma forma (identificador opaco) que la mitigación de
`Legal.md §3.2` ya eligió, así que no habrá que deshacerla cuando ese WI se construya. La respuesta
nominal que da la B es mejor producto, y no vale abrir con ella un frente que hoy es un release blocker.

**El techo de 19 créditos merece su propio WI** — existe hoy, no lo causa este cambio, y bajar
`PageSize` de 25 sería recortar datos de dinero para que entren en un prompt, que es la clase de arreglo
que hay que decidir a propósito y no de pasada.

---

**Esperando decisión. Sin commit, sin cambios de producto, sin escrituras.**
