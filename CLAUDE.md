# CLAUDE.md — Wasnie

This file is read automatically at the start of every Claude Code session. It is the
short, always-on summary of the non-negotiable rules. It does NOT replace the full
docs — it points to them and surfaces the rules that get broken most often.

**§8 (Reglas de desarrollo)** está destilada de defectos reales de este repo y aplica a
TODO trabajo, no solo al que toca las secciones de arriba. Leela antes de empezar un WI.

**Authority order (when in conflict):**
`docs/ARCHITECTURE.md` > `docs/Wasnie_Product_Master_Specification.md` >
`WasnieUi/DESIGN_SYSTEM.md` > everything else. Read the relevant full doc before
non-trivial work; this file is a reminder, not a substitute.

---

## 0. Git — absolute

NEVER run any git command that changes repo state (`add`, `commit`, `push`, `pull`,
`merge`, `rebase`, `checkout` to another branch, `reset`, `stash`). If a commit is
needed, STOP and tell the user. File/build/test/dep/migration-against-local-test-DB
work is auto-approved.

## 1. This is financial software

Wasnie calculates real people's pay. A one-cent miscalculation is a breach of trust,
not a small bug. When unsure, STOP and ask rather than guess. Money math is
critical-risk and MUST have tests (the "no unit tests" policy is OVERRIDDEN for money
code, plan rules, calculation, and Transaction — see Spec §5b.4).

## 2. Docs live OUTSIDE the code projects

`PROJECT_STATUS.md`, `SESSION_LOG.md`, `ARCHITECTURE.md` are at
`<repo>/docs/` — ONE LEVEL ABOVE `WasnieApi/` and `WasnieUi/`. Search there, not only
under the project you're working in. EVERY non-trivial WI ends by updating
`docs/PROJECT_STATUS.md` (+ bump "Last updated") and prepending a `docs/SESSION_LOG.md`
entry. This is mandatory, not optional.

## 3. Inspect before you build

Several "create X" tasks turned out to be "X already exists" (Money, Transaction).
Before creating anything, search the repo for it. If it exists, the task is a refactor
— report what's there and STOP for scoping. Never overwrite working code blind.

## 4. Build before you trust tests

Run `dotnet build` (backend) / `ng build` (frontend) BEFORE relying on test results.
A stale-binary `--no-build` run already produced false failures once. "Tests pass" only
counts against freshly built output. Report the before→after test count every WI.

---

## 5. UI RULES — read these every time you touch the frontend

The frontend is where the design system gets violated most. Before building ANY UI,
open `WasnieUi/DESIGN_SYSTEM.md`. These are the rules that break repeatedly:

### 5.1 Mirror an existing good component — don't invent
The Payees feature is the canonical reference (`app-payee-form`, the payees list +
store). A new feature MUST structurally mirror it: same folder layout
(`create/ form/ detail/ services/ store models`), same store pattern, same form
component pattern. If you find yourself inventing a new structure, you're doing it
wrong — copy Payees.

### 5.2 EVERY form and content block lives inside a `WsCard`
**The #1 repeated bug:** forms rendered directly on the page background, with inputs
floating in the void. FORBIDDEN. A form is wrapped in `<ws-card>` (surface level 2,
`--color-bg-surface` + `border: 1px solid var(--color-border-default)` +
`box-shadow: var(--shadow-card)`). It must look structurally IDENTICAL to the
"Add Payee" form: a contained card centered in the page, not naked fields.
"Cards visually identical to page background" is an explicit forbidden pattern
(DESIGN_SYSTEM "Surface elevation → Forbidden patterns").

### 5.3 Surface elevation is layered — respect it
- Page canvas: `--color-bg-page` (NEVER put this on a card/modal)
- Cards/tables: `--color-bg-surface` + card border + `--shadow-card`
- Inputs: `--color-bg-surface-sunken` (sunken relative to their card — NOT the same
  level as the card)
- Modals/dropdowns: `--color-bg-surface-raised`

### 5.4 Use the Ws primitives only — no native/ad-hoc elements
- Forms: `WsInput`, `WsSelect`, `WsDatePicker`, `WsButton` — NEVER native `<select>`
  or `<input type="date">`
- Lists: `WsTable` + `WsPagination` + `WsEmptyState` — NEVER ad-hoc table styling
- Headers: `WsPageHeader`
- Feedback: `WsToast` / `WsModal` — NEVER `confirm()` or browser dialogs
- Status values: `WsBadge`

### 5.5 Tokens only — never literals
No hex codes, no `rgba(...)`, no Tailwind palette utilities (`text-blue-600`,
`border-slate-300`), no invented radii/paddings/gaps/font-sizes, no inline styles.
Every color/border/spacing comes from a defined token.

### 5.6 Form layout
4+ fields → two-column `.ws-form-grid`. Amount+currency → the `amount-pair` nested
`2fr 1fr` grid. Relationship dropdowns (payee, plan, manager) → full row. One shared
`*FormComponent` per entity (Create and Edit both use it).

### 5.7 Architecture (frontend)
Components NEVER inject `HttpClient` (services own HTTP). NO calculations in components
or templates — money formatting via the existing pipe/helper, never hand-rolled.
Server-side pagination only; search debounced 300ms.

### 5.8 RBAC = hide, don't disable (Spec §5b.6)
Show users what they CAN do. Forbidden actions are HIDDEN via `*hasPermission`, never
shown-but-disabled.

### 5.9 i18n is complete or it's not done
Every new string gets EN + ES + PL keys. No English fallback left in ES/PL.

---

## 6. UI Definition of Done — a frontend WI is NOT complete until:

1. The new feature visually mirrors the Payees equivalent (card wrapper, spacing,
   elevation). **Take a screenshot and compare against the Payees screen before
   reporting done.** If a form's fields float on the page background, it is NOT done.
2. Only Ws primitives used; zero native form elements; zero hex/rgba/Tailwind-palette
   literals.
3. EN/ES/PL all complete.
4. RBAC gating present (hidden, not disabled).
5. `ng build --configuration production` clean; bundle within budget.
6. Frontend tests added; `ng test --no-watch` passes; coverage > 60%.
7. `docs/PROJECT_STATUS.md` + `docs/SESSION_LOG.md` updated.

If you cannot satisfy a rule because a needed primitive doesn't exist, STOP and report
— adding to the design system is a separate decision (DESIGN_SYSTEM §10.3), not
something to improvise mid-feature.

---

## 7. When a rule blocks you

If following a rule seems impossible or wrong for the task, STOP and report the
conflict. Do not silently violate it and do not invent a workaround. Either the rule
needs amendment (a user decision) or the approach is wrong.

---

## 8. Reglas de desarrollo — destiladas de defectos reales (2026-08-25 → 2026-08-31)

Cada una tiene detrás al menos un caso concreto en este repo. **No son principios generales
de ingeniería: son las formas específicas en que ESTE sistema ha fallado.** Aplican a CC y a
quien escriba los WIs.

### A. Sobre la evidencia

**A1 — El código es la verdad.** Todo WI arranca con un Paso 0 read-only. Ninguna premisa del
WI se da por buena: si el código la contradice, el código gana y se reporta. En la sesión que
originó estas reglas, **cinco WIs partían de una premisa falsa**.

**A2 — Verde no es evidencia.** Un test que pasa por un camino que producción no toma no prueba
nada. Ocurrido: las invariantes de `RateTable` estaban probadas y muertas, porque los tests
llamaban a las fábricas y producción deserializaba por propiedades. También: `jsdom` no hace
layout, así que cuatro suites verdes no vieron un defecto de medición.

**A3 — Mirá la salida, no el código que debería producirla.** Seis veces en una semana el arreglo
estaba escrito, era correcto, y aterrizaba en una rama que no se ejecutaba: la clase `--stacked`,
el degradado del botón, la regla 18, las reglas 10e-10h, el bloque de identidad, `formatRate`.
Lo que lo detecta siempre es **el DOM real, el log real, la fila real**.

**A4 — Cuando el front consume un contrato del backend, el fixture sale del endpoint real.**
Ocurrido: enums serializados como strings, fixtures con números, comparaciones falsas para
siempre y tests en verde.

**A5 — Guarda de exit-code.** `cmd | grep` toma el estado de `grep`. Usar `${PIPESTATUS[0]}`.
Ocurrido: una corrida de integración con 682 rojas salió con estado 0 porque Docker no estaba
levantado.

**A6 — Antes de refactorizar el motor, tests de caracterización.** Escritos contra el motor
intacto, con importes **leídos de esa corrida y no calculados a mano**. No se editan después: si
hay que cambiar una expectativa, el refactor alteró algo que no debía.

### B. Sobre el dinero

**B1 — Nunca fallar en silencio.** Si el sistema no puede hacer algo, lo registra. Un
`return Money.Zero`, un `continue` en un `foreach` o una transacción que queda `Pending` para
siempre son la causa raíz de casi todo lo encontrado esa semana. **Registrar no es lanzar una
excepción**: una excepción en un lote mata el lote. Es dejar una fila que alguien pueda ver.

**B2 — Ingerir y marcar, nunca rechazar.** Una venta ocurrió en el mundo real. Si la
configuración está rota, el hecho se registra igual y se marca. Rechazar en la ingesta descarta
el hecho, y ese es el patrón que produjo el dinero invisible.

**B3 — Un campo, un significado.** Un campo que devuelve el mismo valor para dos estados
distintos hace mentir a todo lo que lo lea. Ocurrido: el clawback fundido en `awaitingPayment`;
`NoAssignmentsOrNotVisible` para "no hay" y "no veo"; `IsActive` para "borrada de un borrador" y
"detenida"; un attainment de 0% sellado como `Measured` tanto si se midió como si no había cuota.

**B4 — Congelar y reportar.** Un defecto que toca dinero ya pagado se para y se reporta. No se
corrige sobre la marcha, no se propone script.

**B5 — La bandera se desincroniza; el estado derivado no.** Si algo se puede calcular del dinero,
se calcula. Ocurrido: la cola de cuentas huérfanas deriva de los saldos, y por eso un crédito
nuevo reabre al payee solo.

**B6 — No borrar evidencia.** Nada que registre un hecho vuelve a nulo. Ocurrido: `Activate()`
limpia `DeactivatedAt` y borra la historia, en un producto con ledger append-only.

### C. Sobre lo que ve el usuario

**C1 — Códigos, no prosa.** Todo lo que llegue a pantalla viaja como código con parámetros. Un
motor que emite frases hay que redesplegarlo para corregir una traducción. Molde:
`DomainCodedException` + `PayoutSkipReason` + `RuleSimulationBlocker`.
**Deuda viva: 187 mensajes de dominio en prosa inglesa, en 30 archivos.**

**C2 — El front nunca concatena la clave de traducción.** Nada de `PREFIX.${code}`: un código
desconocido imprimiría un identificador interno. Whitelist explícita, con fallback genérico.

**C3 — La pantalla tiene que decir lo que el sistema hace.** Dos veces en una semana no lo hacía,
en direcciones opuestas: el hint de attainment describía un acumulado creciente y el campo
esperaba un ratio; la confirmación de archivar dice "solo lectura" y en realidad **desasigna a
todos los payees**.

**C4 — Un campo numérico declara su unidad.** Si no la declara, el usuario elige una y no será la
tuya.

### D. Sobre las validaciones

**D1 — Validar al escribir, nunca al leer.** Lo ya guardado se lee siempre, esté malformado o no.
Una validación en el camino de lectura convierte datos históricos en pantallas rotas.

**D2 — Las invariantes van donde pasan todos los caminos.** Una validación en la capa de
aplicación no sirve si hay una vía —una clonación, un job, un converter— que la esquiva.

**D3 — No usar el tipo de dominio como DTO de entrada HTTP.** El serializador lo construye por
propiedades y las fábricas quedan muertas. Tipo de petición propio, que llama a la fábrica.

**D4 — No cerrar la salida de emergencia.** Una validación que impide corregir lo que ya está roto
es peor que la falta de validación. Ocurrido: si el clon de un plan validara, las reglas rotas de
planes activos quedarían congeladas para siempre.

### E. Sobre el método

**E1 — Un WI, un riesgo de reversión.** Un bug de presentación, un cambio de esquema y un refactor
del motor no van juntos. Si el WI falla a medias, el árbol queda inconsistente.

**E2 — Ningún paso opcional decidido por quien lo ejecuta.** "Hacé esto o, en su defecto, aquello"
produce trabajo a medias. Si hay una decisión, se toma antes.

**E3 — Puertas de parada explícitas.** Con la condición y qué hacer al dispararse. Una puerta que
se dispara y ahorra un refactor equivocado vale más que el WI entero.

**E4 — Los comentarios que explican decisiones de negocio se conservan.** Los de
`CommissionCalculator.cs:322-325` (el floor corre después del cap) y
`CreditAllocationService.cs:349-351` (el coste del batch) fueron lo que permitió entender el
motor. *(Archivos añadidos al citarlos: eran dos ficheros distintos y un número de línea suelto
se pudre.)*

**E5 — Medir, no proyectar.** Números crudos de los datos reales. Proyectar a 24 meses desde datos
sembrados da una cifra con decimales y cero valor.

**E6 — Reportar la premisa contradicha.** Cada informe termina listando qué del WI resultó falso.
Es la parte más útil del informe.

# Reglas de desarrollo — Incentra

Destiladas de los defectos reales encontrados entre el 2026-08-25 y el 2026-08-31. Cada una tiene detrás al menos un caso concreto en este repo. No son principios generales de ingeniería: son las formas específicas en que **este** sistema ha fallado.

Van a `CLAUDE.md`. Aplican a CC y a quien escriba los WIs.

---

## A. Sobre la evidencia

**A1 — El código es la verdad.** Todo WI arranca con un Paso 0 read-only. Ninguna premisa del WI se da por buena: si el código la contradice, el código gana y se reporta. En esta sesión, cinco WIs partían de una premisa falsa.

**A2 — Verde no es evidencia.** Un test que pasa por un camino que producción no toma no prueba nada. Ocurrido: las invariantes de `RateTable` estaban probadas y muertas, porque los tests llamaban a las fábricas y producción deserializaba por propiedades. También: `jsdom` no hace layout, así que cuatro suites verdes no vieron un defecto de medición.

**A3 — Mirá la salida, no el código que debería producirla.** Seis veces esta semana el arreglo estaba escrito, era correcto, y aterrizaba en una rama que no se ejecutaba: la clase `--stacked`, el degradado del botón, la regla 18, las reglas 10e-10h, el bloque de identidad, `formatRate`. Lo que lo detecta siempre es el DOM real, el log real, la fila real.

**A4 — Cuando el front consume un contrato del backend, el fixture sale del endpoint real.** Ocurrido: enums serializados como strings, fixtures con números, comparaciones falsas para siempre y tests en verde.

**A5 — Guarda de exit-code.** `cmd | grep` toma el estado de `grep`. Usar `${PIPESTATUS[0]}`. Ocurrido: una corrida de integración con 682 rojas salió con estado 0 porque Docker no estaba levantado.

**A6 — Antes de refactorizar el motor, tests de caracterización.** Escritos contra el motor intacto, con importes leídos de esa corrida y no calculados a mano. No se editan después: si hay que cambiar una expectativa, el refactor alteró algo que no debía.

---

## B. Sobre el dinero

**B1 — Nunca fallar en silencio.** Si el sistema no puede hacer algo, lo registra. Un `return Money.Zero`, un `continue` en un `foreach` o una transacción que queda `Pending` para siempre son la causa raíz de casi todo lo encontrado esta semana. **Registrar no es lanzar una excepción**: una excepción en un lote mata el lote. Es dejar una fila que alguien pueda ver.

**B2 — Ingerir y marcar, nunca rechazar.** Una venta ocurrió en el mundo real. Si la configuración está rota, el hecho se registra igual y se marca. Rechazar en la ingesta descarta el hecho, y ese es el patrón que produjo el dinero invisible.

**B3 — Un campo, un significado.** Un campo que devuelve el mismo valor para dos estados distintos hace mentir a todo lo que lo lea. Ocurrido: el clawback fundido en `awaitingPayment`; `NoAssignmentsOrNotVisible` para "no hay" y "no veo"; `IsActive` para "borrada de un borrador" y "detenida"; un attainment de 0% sellado como `Measured` tanto si se midió como si no había cuota.

**B4 — Congelar y reportar.** Un defecto que toca dinero ya pagado se para y se reporta. No se corrige sobre la marcha, no se propone script.

**B5 — La bandera se desincroniza; el estado derivado no.** Si algo se puede calcular del dinero, se calcula. Ocurrido: la cola de cuentas huérfanas deriva de los saldos, y por eso un crédito nuevo reabre al payee solo.

**B6 — No borrar evidencia.** Nada que registre un hecho vuelve a nulo. Ocurrido: `Activate()` limpia `DeactivatedAt` y borra la historia, en un producto con ledger append-only.

---

## C. Sobre lo que ve el usuario

**C1 — Códigos, no prosa.** Todo lo que llegue a pantalla viaja como código con parámetros. Un motor que emite frases hay que redesplegarlo para corregir una traducción. Deuda viva: 187 mensajes de dominio en prosa inglesa, en 30 archivos.

**C2 — El front nunca concatena la clave de traducción.** Nada de `PREFIX.${code}`: un código desconocido imprimiría un identificador interno. Whitelist explícita, con fallback genérico.

**C3 — La pantalla tiene que decir lo que el sistema hace.** Dos veces esta semana no lo hacía, en direcciones opuestas: el hint de attainment describía un acumulado creciente y el campo esperaba un ratio; la confirmación de archivar dice "solo lectura" y en realidad desasigna a todos los payees.

**C4 — Un campo numérico declara su unidad.** Si no la declara, el usuario elige una y no será la tuya.

---

## D. Sobre las validaciones

**D1 — Validar al escribir, nunca al leer.** Lo ya guardado se lee siempre, esté malformado o no. Una validación en el camino de lectura convierte datos históricos en pantallas rotas.

**D2 — Las invariantes van donde pasan todos los caminos.** Una validación en la capa de aplicación no sirve si hay una vía —una clonación, un job, un converter— que la esquiva.

**D3 — No usar el tipo de dominio como DTO de entrada HTTP.** El serializador lo construye por propiedades y las fábricas quedan muertas. Tipo de petición propio, que llama a la fábrica.

**D4 — No cerrar la salida de emergencia.** Una validación que impide corregir lo que ya está roto es peor que la falta de validación. Ocurrido: si el clon de un plan validara, las reglas rotas de planes activos quedarían congeladas para siempre.

---

## E. Sobre el método

**E1 — Un WI, un riesgo de reversión.** Un bug de presentación, un cambio de esquema y un refactor del motor no van juntos. Si el WI falla a medias, el árbol queda inconsistente.

**E2 — Ningún paso opcional decidido por quien lo ejecuta.** "Hacé esto o, en su defecto, aquello" produce trabajo a medias. Si hay una decisión, se toma antes.

**E3 — Puertas de parada explícitas.** Con la condición y qué hacer al dispararse. Una puerta que se dispara y ahorra un refactor equivocado vale más que el WI entero.

**E4 — Los comentarios que explican decisiones de negocio se conservan.** Los de `:322-325` (el floor corre después del cap) y `:349-351` (el coste del batch) fueron lo que permitió entender el motor.

**E5 — Medir, no proyectar.** Números crudos de los datos reales. Proyectar a 24 meses desde datos sembrados da una cifra con decimales y cero valor.

**E6 — Reportar la premisa contradicha.** Cada informe termina listando qué del WI resultó falso. Es la parte más útil del informe.

---

## F. Tareas que vienen de un ticket de Jira

Ante cualquier tarea que venga de un ticket:

1. Arrancar con un **PASO 0 de SOLO LECTURA**. Verificar cada premisa del ticket contra el código, con `file:line`. El código manda.
2. Si una premisa del ticket es **falsa**, PARAR y reportar. No corregir sobre la marcha.
3. Si el ticket toca **dinero ya pagado**, PARAR y reportar.
4. **NUNCA commitear.**
5. Terminar con la cadena completa, con guarda de exit-code:
   ```
   dotnet build Wasnie.sln --nologo 2>&1 | tail -5; B=${PIPESTATUS[0]}
   dotnet test tests/Wasnie.UnitTests/... --no-build 2>&1 | tail -5; U=${PIPESTATUS[0]}
   dotnet test tests/Wasnie.IntegrationTests/... --no-build 2>&1 | tail -5; I=${PIPESTATUS[0]}
   echo "build=$B unit=$U integration=$I"
   ```
6. Reportar al final **qué premisas del ticket resultaron falsas**.
7. AL TERMINAR, comentar SIEMPRE en el ticket, sin preguntar.
   El comentario lleva:
   - qué se construyó y qué quedó fuera
   - las premisas del ticket que resultaron falsas
   - las decisiones tomadas y POR QUÉ (la alternativa
     descartada y la razón)
   - los file:line relevantes
   - el resultado de cada suite
   - lo que quedó sin verificar y por qué
8. Al EMPEZAR un ticket, después del Paso 0 y antes de construir:
   - mové el ticket de To Do a In Progress
   - dejá un comentario breve: "Empezando. Paso 0 verificado,
     premisas [ok / la premisa X es falsa]. Voy a construir Y."

   Al TERMINAR:
   - mové el ticket de In Progress a In Review
   - dejá el comentario completo (qué se construyó, premisas
     falsas, decisiones y su porqué, file:line, suites, lo no
     verificado)

   NUNCA muevas un ticket a Done. Ese estado es de Rodolfo,
   después de review + commit + runtime.
9. Si en el camino aparece un defecto ajeno al ticket, crear
   un ticket nuevo con la evidencia y enlazarlo. No arreglarlo
   en la misma tanda.