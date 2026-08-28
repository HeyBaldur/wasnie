# Diagnóstico — el asistente no llamó a `simulate_plan_rules`

**Fecha:** 2026-08-27 · **Rama:** AI-CHAT-ASSISTANT · **READ-ONLY: no se cambió ni una línea de producto.**

Fuente de los datos: `WasnieApi/src/Wasnie.Api/logs/wasnie-20260827.log` (Serilog, sink de archivo, ya
existía — no hizo falta instrumentar nada) y el código en el árbol de trabajo.

---

## 0. Resumen

**`simulate_plan_rules` no se invocó ni una vez.** Cero apariciones en **todos** los archivos de log,
no sólo el de hoy.

Y la causa **no** es que la herramienta no estuviera disponible. Estaba registrada, construida y
ofrecida al modelo. La causa son dos cosas, ambas establecidas desde el código:

1. **Las reglas 10e-10h nunca llegan al modelo que decide.** Están en `DataRules`, que (a) sólo se
   arma cuando **ya corrió** una herramienta y (b) va dirigido al modelo que **redacta**, no al que
   **elige**. Es una instrucción correcta entregada al lector equivocado.
2. **El prompt del dispatcher — el único sitio que puede afectar la elección — no menciona la
   simulación.** No hay una sola palabra sobre hipotéticos, "cuánto pagaría" o "simular".

★ El propio repositorio ya documentó este modo de fallo, en el archivo que lo causa
(`AssistantToolRunner.cs`, comentario de `IdentifierRules`):

> *"It used to live in `AssistantPrompt.DataRules` as rules 10b/10c, addressed to the model that
> composes the ANSWER — a model that never calls a tool and has no arguments to fill in. It was a
> correct instruction delivered to the wrong reader, and it had no effect on any lookup. Worse, it
> LOOKED like the fix."*

Es exactamente lo que hice al poner 10e-10h en `DataRules`.

---

## 1. Qué se llamó realmente — **MEDIDO**

Seis turnos del asistente hoy. Tres ejecuciones de herramienta:

| hora | herramienta | resultado |
|---|---|---|
| 16:04:47 | `get_transaction` | — |
| 16:06:02 | `get_plan_rules` | — |
| 16:38:36 | `get_plan_rules` | *"plan name required"* |

Y los turnos, por la línea del router:

| hora | secciones elegidas | ¿herramienta? |
|---|---|---|
| 16:04:40 | s6, s11 | `get_transaction` |
| 16:05:57 | NINGUNA | `get_plan_rules` |
| 16:08:20 | s11, s13 | ninguna |
| 16:37:27 | NINGUNA | `get_plan_rules` (16:38:36) |
| 16:37:56 | s1, s2, s3 | ninguna |
| **16:43:07** | **s6, s8, s5, s13** — *"4. Plan and Rules", "6. SplitAtQuota — the accelerator question"* | **NINGUNA** |

**★ El turno decisivo es el de 16:43:07 y no invocó ninguna herramienta.** Es el último del archivo,
sus secciones son las de una pregunta sobre reglas de un plan, y encaja con la pregunta reportada.

> **Salvedad, dicha como salvedad:** el log **no registra el contenido del mensaje** (por diseño), así
> que la correspondencia entre ese turno y la pregunta exacta de Rodolfo es **inferida por hora y por
> las secciones elegidas**, no medida. Lo que **sí** está medido y no depende de esa inferencia:
> `simulate_plan_rules` no se invocó en ningún turno, de ningún día.

---

## 2. La herramienta **sí** estaba disponible — **MEDIDO**

Descartada la explicación alternativa obvia (que el proceso fuera anterior al registro):

| evento | hora |
|---|---|
| registro del tool escrito (`DependencyInjection.cs`) | 16:22:48 |
| `SimulatePlanRulesTool.cs` escrito | 16:23:01 |
| **binarios compilados** (`Wasnie.Infrastructure.dll` en `bin`) | **16:24:41** |
| **la API reinicia** (`Now listening on`) | **16:36:56** |
| turno decisivo | 16:43:07 |

El proceso que atendió ese turno arrancó **12 minutos después** de compilarse el registro. Y
`AssistantToolRunner.SelectAsync:100-103` pasa **todos** los esquemas registrados al proveedor:

```csharp
request = await provider.SelectToolAsync(
    BuildSelectionMessages(question, history),
    _tools.Select(t => t.Schema).ToList(),   // ← todos, sin filtro
    cancellationToken);
```

**No hay dispatcher previo que filtre herramientas (3.6).** El modelo vio los cinco esquemas,
incluido el del simulador, y eligió no usarlo.

---

## 3. ★★ Las reglas 10e-10h **no llegaron** — **MEDIDO EN CÓDIGO**

Dos motivos independientes; cualquiera de los dos basta.

### 3.1 — Sólo viajan después de que corrió una herramienta

`AssistantPrompt.cs:929-931`:

```csharp
var hasData = !string.IsNullOrWhiteSpace(toolData);
var dataBlock = hasData
    ? $"\n{DataRules}\n\n{DataHeader}\n{toolData}\n{DataFooter}\n\n{DataReminder}\n"
    : string.Empty;
```

`DataRules` contiene 10e-10h. En el turno donde el modelo debe decidir si simular, `toolData` está
vacío → el bloque no existe → **la regla que le dice que use la herramienta es inalcanzable
exactamente en el momento en que tendría que aplicarla.**

### 3.2 — Y aunque viajaran, van al lector equivocado

El flujo es de **dos llamadas** (`AssistantToolRunner.cs:7-15`):

1. el router elige secciones del manual;
2. **`SelectAsync` — una llamada aparte cuyo único trabajo es elegir herramienta**, guiada por
   `SelectionInstructions`;
3. la llamada generadora recibe manual + datos, y es la que lee `DataRules`.

`DataRules` sólo lo ve el paso 3. **El paso 2 nunca lo ve.** Poner ahí "usá la herramienta" no puede
cambiar ninguna elección.

### 3.3 — Y sólo hay **una ronda** de herramientas por turno

Del mismo comentario: *"One round of tool use per turn — enough for a single read-only lookup, and the
ceiling is deliberate."*

Consecuencia para el turno de 16:38:36: elegido `get_plan_rules`, **no había forma de llamar además al
simulador**. Aunque el modelo hubiera querido las dos cosas, la arquitectura permite una.

---

## 4. Qué dice el prompt que **sí** decide

`AssistantToolRunner.SelectionInstructions` es el único texto que puede cambiar la elección. Enumera
tres casos:

- una transacción concreta por referencia;
- **cómo paga un plan, qué reglas o tarifas tiene, cómo está configurado, o por qué una comisión salió
  como salió**;
- las asignaciones de un payee.

**No menciona simulación, hipotéticos, "cuánto pagaría", "si vendo X" ni cantidades.** Y cierra con
dos frases que empujan en contra:

> *"Call NO tool when the message is about … how the product works in general."*
> *"Never call a tool just in case."*

★ La pregunta *"¿cuánto genera cada rule?"* encaja **literalmente** en el segundo caso — *"why a
commission came out the way it did"* — que apunta a `get_plan_rules`. El dispatcher no eligió mal
según sus instrucciones: **eligió bien según unas instrucciones que no saben que el simulador
existe.**

---

## 5. El esquema, tal como lo ve el modelo (3.4)

Descripción literal de `SimulatePlanRulesTool.Schema`:

> *"Compute what each rule of a plan would pay for a hypothetical sale, using the real commission
> engine. Read-only; nothing is created. Use it whenever the user asks how much a plan or a rule WOULD
> pay for an amount or a number of units — never work the figures out yourself."*

Parámetros: `planId`, `planName`, `amount` (**el único requerido**), `quantity`, `attainmentPct`,
`priorCumulative`, `quotaTarget`.

**Cotejo de vocabulario contra cómo pregunta un usuario real:**

| el usuario dice | ¿aparece en la descripción? |
|---|---|
| "cuánto genera cada rule" | **no** — dice *"how much a plan or a rule WOULD pay"* |
| "tengo una transacción de 7.850" | **no** — dice *"a hypothetical sale"* |
| "con 5 unidades" | parcial — *"a number of units"* |
| "simular" | **no** |

La descripción está escrita en el vocabulario del producto (*hypothetical sale*), no en el de la
pregunta (*tengo una transacción de 7.850*). Y el usuario preguntó **en español**, contra una
descripción en inglés — lo que no es un impedimento para un modelo multilingüe, pero reduce el
solapamiento léxico que gobierna esta elección.

---

## 6. La causa, separando lo medido de lo supuesto

### Medido

- `simulate_plan_rules`: **0 invocaciones**, en todos los logs.
- La herramienta **estaba registrada y ofrecida** en el proceso que atendió el turno (binarios de
  16:24:41, arranque 16:36:56).
- **No hay filtro previo de herramientas**: se ofrecen las cinco.
- `DataRules` (donde viven 10e-10h) **sólo se arma con `toolData` no vacío** y **sólo lo lee el modelo
  que redacta**.
- **Una sola ronda** de herramienta por turno.
- `SelectionInstructions` **no menciona** simulación ni hipotéticos.
- El turno de 16:43:07 **no invocó ninguna herramienta**.

### Supuesto (dicho como supuesto)

- Que el turno de 16:43:07 es la pregunta exacta de Rodolfo — inferido por hora y secciones, no medido:
  el log no guarda el contenido del mensaje.
- **Por qué** el dispatcher no eligió el simulador en ese turno concreto. Lo medible es que no lo
  eligió; el peso relativo del vocabulario, del "never call a tool just in case" y del solapamiento con
  `get_plan_rules` **no se puede establecer desde el código ni desde los logs**. Queda como **NO
  ESTABLECIDO**.

### Lo que **no** es la causa

- No es que la herramienta no estuviera registrada.
- No es un esquema inválido.
- No es un rechazo del proveedor: **0** líneas de *"tool call the provider rejected"* hoy.
- No es que el resultado llegara mal: **no llegó ningún resultado, porque no hubo llamada.**

---

## 7. Recomendación — **no implementada**

En orden de impacto esperado. Las tres primeras son sobre **el prompt del dispatcher**, que es el
único lugar donde una instrucción sobre elegir herramientas puede tener efecto.

1. **★★ Mover la instrucción a `SelectionInstructions`.** Un caso explícito: *"si el mensaje plantea
   un importe o una cantidad hipotéticos — 'cuánto pagaría', 'si vendo X', 'una transacción de N con M
   unidades' — llamá al tool de simulación, no al de configuración."* Es el arreglo directo, y es el
   mismo movimiento que ya se hizo una vez con 10b/10c → `IdentifierRules`.

2. **Desambiguar contra `get_plan_rules` de forma explícita.** Hoy la frase *"why a commission came out
   the way it did"* absorbe la pregunta. Conviene el mismo tratamiento que recibió el par
   payee/plan: decir cuál es cuál y por qué se confunden. *"Cómo está configurado"* → configuración;
   *"cuánto daría sobre 7.850"* → simulación.

3. **Revisar la descripción del esquema** para que use el vocabulario de la pregunta y no el del
   producto.

4. **Decidir qué hacer con 10e-10h.** Tal como están son inertes para la elección, pero **no son
   inútiles**: en el turno donde el simulador *sí* corra, gobiernan cómo se reporta el resultado
   (orden de los pasos, `Supplied`/`Defaulted`, no totalizar). La recomendación es **dejarlas** y
   añadir la instrucción de elección en el dispatcher — no moverlas.

5. **★ Y el punto que ninguna de las anteriores resuelve: una sola ronda por turno.** *"¿Cómo está
   configurado y cuánto daría?"* necesita dos herramientas. Con el techo actual, cualquier
   desambiguación que se escriba obliga al modelo a elegir una de las dos y a fallar la otra mitad de
   la pregunta. Levantar el techo es *"a different feature with a different cost profile"* según el
   propio comentario del archivo — **decisión de Rodolfo**, no algo a cambiar dentro de un arreglo de
   prompt.

6. **★ Un test que habría visto esto.** Los que hay verifican registro y esquema válido; ninguno
   prueba que el modelo **elija** la herramienta. Un test de selección —dada la pregunta, ¿qué tool
   devuelve el dispatcher?— necesita una llamada real al proveedor, así que no es un test unitario;
   pero sin algo así, el próximo tool que se agregue puede quedar igual de invisible y todo seguirá en
   verde.

---

## 8. Nota de método

Este es el **quinto** caso en la sesión del mismo patrón: el arreglo escrito, correcto, y aterrizado
en una rama que no se ejecuta. Lo que lo resolvió en los cinco no fue razonar sobre qué debería
ejecutarse, sino **mirar qué se ejecutó** — acá, un archivo de log que ya existía y que respondió la
pregunta en dos consultas.
