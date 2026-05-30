# CLAUDE.md — Wasnie

This file is read automatically at the start of every Claude Code session. It is the
short, always-on summary of the non-negotiable rules. It does NOT replace the full
docs — it points to them and surfaces the rules that get broken most often.

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
