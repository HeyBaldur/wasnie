# Wasnie — Configuration Guide

**What this is:** a walkthrough of how Wasnie is configured and how it calculates, with worked
numbers and what appears on screen at each step.

**Who it's for:** two readers at once —
- **Operators / owner** — a reference for how the system actually behaves.
- **New customers** — a first-use guide, in the order you'd really do it.

**Verified against the code on 2026-07-30.** Every statement about Wasnie's behaviour in this
document was checked by reading the source, not from memory, and cites `file:line` so you can
re-check it. (The 2026-07-27 pass reconciled the trigger, plan-attribution, transaction-lifecycle and
deal-lost/recovery sections. The 2026-07-30 pass documented the **clawback subsystem, termination /
orphaned accounts, the ledger & statement, and permissions** — all four shipped after the previous
pass and are new sections [15](#15-clawback)–[18](#18-permissions-and-roles).) **If the calculation
engine changes, this document must be re-verified** — a guide that misdescribes the engine is worse
than no guide. The highest-risk sections are [Rate tables](#5-rate-tables),
[SplitAtQuota](#6-splitatquota--the-accelerator-question), [Attainment](#7-attainment) and
[Clawback](#15-clawback).

**Related docs:** `Pay_Run_Model.md` (why pay runs exist — design rationale),
`Wasnie_Product_Master_Specification.md` (product scope), `ARCHITECTURE.md` (engineering rules).
This guide describes **as-built behaviour**; where it disagrees with the spec, this document
reflects what the code does and says so explicitly.

---

## Coverage checklist (this document is written in passes)

Documenting every area of Wasnie against the real code is more than one sitting. This checklist is
the resume point: each pass takes the next unticked area, documents it **completely** — what it is,
how to configure it, use cases (happy path + edges) with concrete numbers, validations/errors, and
permissions — and ticks it here. Areas already covered by the older narrative sections are marked
with the section that covers them.

| Area | Status | Where |
|---|---|---|
| Plans (create, version/clone, activate, period, currency) | [x] | [4](#4-plan-and-rules), [4.5](#45-plan-lifecycle-and-assignments) |
| Plan clawback policy (maturation + cap %) | [x] | [15.1](#151-configuration-the-policy-is-opt-in-per-plan) |
| Rules (trigger, measurement, rate tables, modifier/cap/floor) | [x] | [4](#4-plan-and-rules), [5](#5-rate-tables), [6](#6-splitatquota--the-accelerator-question) |
| Quotas | [x] | [8](#8-quotas) |
| Payees (creation, required fields) | [~] | [3](#3-payees) — **states and the Terminated lifecycle still to document** |
| Assignments (exact-period match, explicit status/date contract) | [~] | [1](#1-the-model-on-one-page), [4.5](#45-plan-lifecycle-and-assignments) — **the list contract (`status`, `dateFrom`/`dateTo`, no magic default) not yet written up** |
| Transactions (ingest, lifecycle, deal-lost / recovery) | [~] | [9](#9-transactions) — **`ProcessImmediately`, the sort whitelist + 400, and the compensation-period filters not yet written up** |
| Pay runs and payouts | [~] | [11](#11-pay-runs-and-payouts) — **terminated exclusion and the residual payout are in [16](#16-termination-and-orphaned-accounts); the period-filter fix not yet written up** |
| **Clawback (the whole subsystem)** | [x] | [15](#15-clawback) |
| **Termination and orphaned accounts** | [x] | [16](#16-termination-and-orphaned-accounts) |
| **Ledger / statement** | [x] | [17](#17-ledger-and-statement) |
| **Permissions and roles** | [x] | [18](#18-permissions-and-roles) |
| Dashboard (Requires action, alerts, metrics) | [ ] | — |
| CRM integration (HubSpot OAuth, sync, mapping, deal-lost → churn) | [ ] | `HUBSPOT_INTEGRATION_DESIGN.md` covers the design; the **operator-facing** use cases are not written |
| Imports (Excel wizards, field requirements, consent) | [ ] | — |
| Settings (field requirements, category mappings) | [ ] | — |
| Subscription / billing (tiers, limits, portal) | [ ] | — |

**Numbering note.** Sections added in later passes are appended (15, 16, …) rather than inserted in
narrative order, so existing links and any circulated PDF page references stay valid. Read
[15](#15-clawback)–[17](#17-ledger-and-statement) directly after [11](#11-pay-runs-and-payouts) if you
are reading front to back.

---

## How to read this document

Two kinds of content appear here, and they are never mixed:

> ✅ **In Wasnie** — implemented and verified. Safe to rely on and to demo.

> 📚 **Industry concept — NOT implemented in Wasnie.** Context only. Do not present as a feature.

Everything is ✅ unless explicitly marked 📚. A consolidated list of the 📚 items is in
[section 13](#13-industry-concepts-not-implemented).

---

## 1. The model on one page

```
Plan  ──────────────►  Rules (1..n)
 │                       │
 │                       ├── Trigger      (when this rule fires)
 │                       ├── Measurement  (Revenue or Units)
 │                       ├── Rate Table   (Flat | Tiered | Attainment-based)
 │                       └── Modifier / Cap / Floor  (optional adjustments)
 │
 ├──► Assignment (payee ↔ plan)   period MUST EQUAL the plan's period
 │
 └──► Quota (payee + plan)        period MUST BE CONTAINED IN the plan's period


Transaction ──► Credit (one per matching rule) ──► PayoutLine ──► Payout ──► Pay Run
```

The two halves are worth separating in your head:

- **Setup** — Plan, Rules, Assignment, Quota. Done once per period/season.
- **Flow** — Transactions arrive, become Credits, get gathered into Payouts, closed by a Pay Run.

### Period relationships (deliberately asymmetric)

| Relationship | Rule | Where |
|---|---|---|
| Assignment ↔ Plan | **exact equality**, enforced | `AssignPlanToPayeeHandler.cs:40-46` |
| Quota ⊆ Plan | **containment** (partial overlap rejected) | `QuotaPeriodGuard.cs:19-33` |
| Pay Run ↔ Quota | **no relationship at all** | see [section 10](#10-measurement-period-vs-payment-period) |

That asymmetry is the reason "monthly quotas inside a quarterly plan" works: the quota may be
shorter than the plan, but the assignment may not.

---

## 2. Setup order (first use)

The app's own empty states describe this order, and it's the order to follow:

1. **Payees** — the people being paid.
2. **Plan** — the container, with an effective period.
3. **Rules** — how commission is computed. *"Add compensation rules to define how payouts are calculated."*
4. **Assignment** — connect payee ↔ plan.
5. **Quota** — only needed for attainment-based rules.
6. **Transactions** — arrive from HubSpot, Excel, or manual entry.
7. **Pay Run** — *"Use 'Calculate Pay Run' to process a period for all assigned payees."*

---

## 3. Payees

**What it is.** A person who can earn commission.

**How to configure.** *Payees → New Payee*. Full name and employee code are **always required** —
they are not configurable and cannot be turned off. Everything else (email, hire date, role,
manager, employment type, location) is governed per tenant in *Settings → Field requirements*.

Full name and employee code are absent from the configurable catalog entirely
(`PayeeFieldNames.cs`), so they can't be relaxed even via the API — the update endpoint rejects
them with *"not in the configurable catalog"*.

**On screen.** *Settings → Field requirements* lists each configurable field with a
**Required / Optional** toggle. Both the manual form and the import wizard read that same setting,
so they always agree.

**Hire date note.** Hire date is informative only — it is not used in any commission calculation.

---

## 4. Plan and Rules

**What it is.** A **Plan** is a named container with an effective period and a currency. It holds
one or more **Rules**. Each rule independently decides whether it fires and how much it pays; a
transaction that matches three rules produces three credits.

**How to configure.** *Plans → New Plan*, then add rules. Each rule has four parts:

### 4.1 Trigger — when the rule fires

A rule fires on **every transaction by default**. Narrowing it is opt-in.

**On screen.** The rule form has a collapsible section headed **"Trigger (optional)"** with an on/off
toggle, and **the toggle starts off**. Leave it off and the rule applies to all transactions — there is
no "all transactions" option to pick, because that is simply what a rule does until you restrict it.
Turn the toggle on to reveal the **AND** / **OR** selector and the **Add Condition** button, then add
one or more conditions; the rule then fires only on transactions that satisfy them.

The screen's own tooltip on that section states the same rule: *"If enabled, this rule only applies when
the conditions are met. Without a trigger, the rule always runs on every matching transaction."*

> ✅ **"Applies to all" is the absence of conditions, not a setting.** With the toggle off the form
> sends no trigger at all and the domain substitutes `Trigger.Always()`
> (`Plan.cs:97,136` — `trigger ?? Trigger.Always()`), which is a trigger with an empty condition list;
> the engine then short-circuits to fire on everything (`CommissionCalculator.cs:33` —
> `if (trigger.Conditions.Count == 0) return true`). Turning the toggle **on** but adding **no**
> conditions lands on that same empty list and behaves identically — so the rule is "all transactions"
> whenever it has zero conditions, however you got there. Read-only screens render this state as
> *"Applies to all transactions (no conditions)."*

> ✅ **The condition Field is a dropdown fed by the engine's own catalog** — not free text. The list
> comes from `GET /api/plans/trigger-fields` (`TriggerFieldCatalog.cs:39-66`), so the UI can only offer
> a field the engine actually reads; a name the engine never heard of can no longer be typed. The eight
> fields are: `transactionamount`, `transactiondate`, `quantity`, `source`, `currency`, `productsku`,
> `productname`, and `category`.
>
> ✅ **Operators are derived from the field's value type**, so the UI never offers one the engine
> ignores (`TriggerFieldCatalog.cs:80-97`):
> - **String** fields (`source`, `currency`, `productsku`, `productname`, `category`) →
>   `Equal` / `NotEqual` / **`In`** / **`NotIn`** (`In` / `NotIn` read a value *set*, e.g.
>   `productsku In {LAP-12, DELL-01}`).
> - **Number** / **Date** fields (`transactionamount`, `quantity`, `transactiondate`) →
>   `Equal` / `NotEqual` / `>` / `>=` / `<` / `<=`.
>
> ✅ **The trigger is validated server-side at save** — a rule with an unknown field, an operator the
> field does not support, or an `In` with no set is rejected, not saved silently. Legacy conditions
> that no longer match a real field are shown with a warning rather than hidden.
>
> ✅ **For `category`, the value is also a picker** built from the tenant's real categories, with an
> explicit "use another value" escape hatch — so a typo can no longer save a rule that never fires
> (see 9, category enrichment).

### 4.2 Measurement — what is being measured

> ✅ **Revenue** (uses the transaction amount) and **Units** (uses the transaction quantity) are the
> only two offered, by a deliberate allowlist (`rule-form.component.ts:87-93`).

The underlying enum also contains `Margin`, `Attainment` and `Custom`, but they are not selectable,
and if injected via the API they compute as Revenue — the engine only branches on `Units`
(`CreditAllocationService.cs:207,225-227`). `MeasurementType.Attainment` is **not** how
attainment plans work; that is the rate table's job (see 5.3).

**Units restriction:** Units measurement only supports a **Flat** rate table. The domain rejects
anything else (`Rule.cs:34-40`), and the UI disables the other rate-table buttons and forces Flat.

> ⚠️ **Aggregation and Source field do nothing.** `Measurement` carries `Aggregation`
> (Sum/Average/Max/Min/Count) and `SourceField`, but **neither is read by the engine anywhere**.
> The UI correctly hides them and pins Sum / `"amount"`.

### 4.3 Rate table

See [section 5](#5-rate-tables).

> ⚠️ **Rates are entered as a decimal fraction: `0.05` is 5%, `1.00` is 100%.** Repeated here on
> purpose — a question about configuring a plan does not always retrieve section 5, and getting this
> backwards misconfigures what people are paid. Full explanation, including the Units exception, in
> [section 5](#5-rate-tables).

### 4.4 Modifier, Cap, Floor — applied in that order

Applied after the rate table, in sequence: **modifier → cap → floor**
(`CreditAllocationService.cs:252-254`).

> ⚠️ **All three modifier types behave identically.** `ModifierType` is
> `Accelerator | Multiplier | Spiff`, and `ApplyModifier` multiplies the commission by `Factor`
> regardless of which you pick (`CommissionCalculator.cs:244-250`, comment: *"Spiff: V1 stub —
> treat Factor as multiplier"*). The type is a label with no behavioural effect today.
>
> *(The master spec lists a "decelerator" type — it does not exist in the code. To decelerate, use a
> factor below 1.0.)*

> ⚠️ **Cap is always per-transaction.** The rule form now offers **only "Per transaction"** as the
> scope and defaults to it (`rule-form.component.ts:116-117,156`), so there is nothing to get wrong —
> the old "set the scope explicitly or it does nothing" trap is gone. The engine applies a cap only
> for the per-transaction scope (`CommissionCalculator.cs:252-264`). The backend enum still carries
> *Per period* and *Total* for future use, but they are not selectable and any request with a
> non-per-transaction scope is rejected outright — *"Only Per Transaction cap scope is currently
> supported."* (`AddRuleToPlanHandler.cs:31-33`, `UpdateRuleHandler.cs:31-33`). A cap in a different
> currency to the commission is still skipped silently (`CommissionCalculator.cs:258-259`).

**Floor** works as expected (raises commission up to the floor amount).

**On screen.** The rule form shows Measurement and Rate table (three buttons) as always-visible
sections, plus **four** collapsible sections that each start toggled **off**: **Trigger (optional)**,
Modifier, Cap and Floor. Leaving one off means the rule has no trigger / no modifier / no cap / no
floor — off is the normal state, not an incomplete one.

### 4.5 Plan lifecycle and assignments

**Archiving a plan deactivates its assignments.** Archiving sets every Active assignment of the plan
to Deactivated in the same operation (`ArchivePlanHandler.cs:43-49`), so an archived plan drops out
of processing and out of the "pending eligible" lists — a payee is no longer resolved against it.

> ✅ **A payee can be assigned to several active plans at once** (e.g. a base plan plus another).
> When a transaction for that payee is processed it is **credited to EVERY applicable plan**, not one:
> the allocator iterates all eligible assignments (`CreditAllocationService.cs:183` `ResolveAssignments`,
> which returns a list) and writes one credit per plan. The old "one plan by shortest-period tie-break"
> rule was removed — it silently decided how much commission was paid, which was a real bug.
>
> ✅ **When there is genuine ambiguity — a payee on 2+ eligible plans and no plan stated — the admin
> chooses.** Manual entry requires picking the plan (`SelectedPlanAssignmentId`, re-validated server-side;
> `CreditAllocationService.cs:190-192` `ResolveSelected`). Excel and HubSpot, where no human is present at
> load time, **fail loud** instead of guessing: the transaction is left Pending and uncredited and appears
> on the dashboard's "needs attention" card under **ambiguous attribution**, grouped by payee — the fix is
> to deactivate the assignment that should not apply.

---

## 5. Rate tables

Three types, all selectable.

> ### ⚠️ RATE INPUT FORMAT — read this before configuring any rate
>
> **Every `Rate` field in Wasnie is entered as a DECIMAL FRACTION, never as a whole percentage.**
>
> | You want | You enter |
> |---|---|
> | 5% | `0.05` |
> | 10% | `0.10` |
> | 8.2% | `0.082` |
> | 100% | `1.00` |
>
> Entering `5` does not mean 5% — it means **500%**. Entering `100` means **ten thousand per cent**.
>
> This is not a UI convention, it is what the engine computes: `CommissionCalculator.cs:166` does
> `baseAmount.Multiply(rateTable.FlatRate)`, tiered does `inTier * tier.Rate`
> (`CommissionCalculator.cs:193`), and attainment does `baseAmount.Multiply(tier.Rate)`
> (`CommissionCalculator.cs:214`). The rate is a multiplier in all three. The form's own hint says the
> same thing: *"Enter as decimal, e.g. 0.05 = 5%"*.
>
> **The percentages written throughout this document — "10% flat", "8.2% up to quota" — describe what a
> rate MEANS, not the keystrokes.** A rate meaning 10% is typed `0.10`.
>
> **Two different fractions, and neither needs converting.** §5.3's `AttainmentFrom` / `AttainmentTo`
> are fractions of the QUOTA (`1.0` = 100% of quota) — a different quantity from the Rate, expressed in
> the same decimal convention. There is nowhere in Wasnie where a rate or a threshold is typed as a
> whole percentage, so there is no conversion to remember and no place to get it backwards.
>
> **One exception, and it is not a percentage at all:** when a rule's Measurement is **Units**, the Flat
> rate is **money per unit** (`ComputeUnitsCommission`, `CreditAllocationService.cs:356`). `2.00` there
> means €2.00 per unit, not 200%.

### 5.1 Flat

One rate applied to the whole base amount.

**Example.** 10% flat, deal of €50,000 → **€5,000.00**

### 5.2 Tiered — absolute amounts, marginal

`RateTier` is `From` / `To` / `Rate`, where From and To are **absolute currency amounts**, not
percentages. The calculation is **marginal**: each tier's rate applies only to the portion of the
amount inside that tier's band (`CommissionCalculator.cs:162-187`).

**Example** (from the engine's own test, `CommissionCalculatorTests.cs:149-163`):

| Tier | Rate | Portion of an €800 deal | Commission |
|---|---|---|---|
| 0 – 500 | 5% | 500 | 25.00 |
| 500 + | 10% | 300 | 30.00 |
| | | | **55.00** |

Not €80 (which is what a single-bracket lookup would give).

> ⚠️ **Tiered resets on every transaction.** The walk starts from zero for each transaction and
> never consults period-to-date totals. One €800 deal earns €55; two €400 deals earn
> €20 + €20 = €40. If you want tiers that accumulate across a period, that is **attainment-based
> with SplitAtQuota**, not Tiered.
>
> Also, `From` is used only to derive each tier's width. A gap between tiers, or a first tier that
> doesn't start at 0, will silently mis-bracket — the model validates ascending non-overlap but not
> contiguity (`RateTable.cs:23-34`).

> ### ⚠️ Tiered is NOT available when Measurement is Units
>
> **You cannot band by quantity.** A rule with `Measurement = Units` and any rate table other than Flat
> is rejected by the domain when you try to save it (`Rule.cs:36-39`):
>
> > *"Units measurement only supports a Flat rate table. Tiered and Attainment rate tables are not
> > supported for unit-based commission."*
>
> The UI enforces the same thing earlier: choosing **Units** switches the rate table to **Flat** and
> disables the other two buttons (`rule-form.component.ts`).
>
> **So a scheme like "€50 each for the first 10 units, €75 each above 10" is not supported in this
> version.** There is no configuration that expresses it — not Tiered, not Attainment. Say so rather
> than approximating it.
>
> **Why it does not simply work anyway:** Tiered's `From` / `To` are **absolute currency amounts of the
> transaction**, not quantities (see the table above). Even if the combination were allowed, the bands
> would slice the deal's VALUE, never its unit count — "1 to 10" would mean €1 to €10, not one to ten
> units.
>
> **What Units does support:** exactly one rate per unit, applied to the whole quantity —
> `ratePerUnit × quantity` (`ComputeUnitsCommission`, `CommissionCalculator.cs:159-160`). Ten units at
> `2.00` is €20.00; a hundred units at `2.00` is €200.00. The rate never changes with volume.
>
> **The nearest supported alternatives**, both of which change what is being measured:
>
> - **Tier on the transaction's value instead of its quantity** — Revenue measurement with a Tiered
>   table. This is a different rule ("more expensive deals earn a higher rate"), not a volume discount,
>   and it resets on every transaction (see the warning above).
> - **Two rules with different triggers**, if some field on the transaction distinguishes the cases.
>   Note that the trigger cannot read quantity thresholds; it filters on transaction fields.
>
> **If a Units rule ever ends up with a non-Flat table** — the domain blocks it, so this would mean data
> written around the domain — the engine does **not** fail loudly. It logs an error and sets that
> commission to **zero** (`CreditAllocationService.cs:358-365`). The symptom is someone being paid
> nothing, not an error message, which is why the restriction is worth knowing before configuring
> rather than after a pay run.


### 5.3 Attainment-based — fractions of quota

`AttainmentTier` is `AttainmentFrom` / `AttainmentTo` / `Rate`, where From/To are **fractions of the
quota**: `1.0` = 100% of quota. They are multiplied by the quota target to get absolute money
(`CommissionCalculator.cs:227-230`).

This type **requires a quota** for the payee+plan. How it behaves depends entirely on the
**Split commission at quota** toggle — see the next section.

---

## 6. SplitAtQuota — the accelerator question

This is the single most consequential toggle in the product, and the one customers ask about first.
It appears only for attainment-based rate tables.

Take an accelerator of **8.2% up to quota, 9.2% above it**, a quota of **48,000**, and revenue of
**50,000**.

### Toggle ON — only the excess is accelerated

The engine splits the revenue at the quota boundary and pays each tier's rate on its own slice
(`CommissionCalculator.cs:212-240`).

| Slice | Rate | Commission |
|---|---|---|
| 0 → 48,000 (up to quota) | 8.2% | 3,936.00 |
| 48,000 → 50,000 (the excess) | 9.2% | 184.00 |
| **Total** | | **4,120.00** |

### Toggle OFF — one bracket rate for the whole transaction

The engine looks up the single tier matching the attainment reached **before** this transaction, and
applies that one rate to the entire transaction (`CommissionCalculator.cs:189-204`;
`attainmentPct` is sourced at `CreditAllocationService.cs:176-178` from credits already committed).

As one €50,000 deal with no prior revenue: attainment before the deal is 0, so the 8.2% bracket
applies to all of it → **4,100.00**.

Across several deals, each is rated by the attainment reached before it, and **earlier deals are
never re-rated**.

### Real anchor

The verified production case: quota **€250,000**, revenue **€277,880.25**, tiers **4% / 7%**:

```
250,000.00 × 4%  = 10,000.0000
 27,880.25 × 7%  =  1,951.6175
                  ────────────
                   11,951.6175   →  €11,951.62
```

This matches the figure validated when the split feature shipped, which confirms **ON = excess-only**
against real data. The same revenue under a whole-amount policy would be €19,451.62.

> ⚠️ **Neither setting pays "the high rate on everything, retroactively."** If a customer says
> *"once they beat quota, the accelerated rate applies to all their revenue for the period"*, that
> is a third policy Wasnie does **not** implement in either toggle state. ON is excess-only; OFF is
> per-transaction bracket lookup with no retroactive re-rating. Confirm which they mean before
> answering.

> ⚠️ **ON with no quota pays zero.** If SplitAtQuota is on and the payee has no Active or Closed
> quota covering the transaction date, commission is **zero** with a logged warning
> (`CreditAllocationService.cs:230-238`). The quota becomes mandatory for earning.

**On screen.** The toggle is labelled **"Split commission at quota"** with an info icon whose
tooltip explains both states accurately. It's a hover tooltip, so the distinction isn't visible at
a glance while toggling.

---

## 7. Attainment

**Formula** (`AttainmentPercentage.cs:26-31`):

```
attainment = achieved ÷ target
```

rounded to 4 decimals (banker's rounding), floored at 0, **not capped** — 120% attainment is
represented as `1.20`.

**`achieved`** is the sum of `Transaction.Amount` (the gross sale, *not* the commission) across
non-superseded credits for that payee+plan whose transaction date falls in the quota period; for
Units quotas it sums `Quantity` instead (`QuotaAttainmentService.cs:83-104,143-162`).

> ⚠️ **The sum covers the whole quota period, not "up to today."** The as-of date selects *which
> quota applies*, but does not bound the sum (`QuotaAttainmentService.cs:98-99` vs `:56`). It
> behaves like a running to-date figure only because later-dated transactions usually haven't been
> ingested yet. **Backdating a deal, or importing out of date order, changes attainment for
> transactions already processed.** The batch job processes in transaction-date order so the normal
> path is correct; the caveat matters for backfills and corrections.

> 📚 **Weighted averages — NOT implemented.** Some comp designs blend attainment across
> sub-periods using weighted averages. Wasnie has no weighting anywhere; the word does not appear
> in the source. Attainment is always a single division over one quota period.

---

## 8. Quotas

**What it is.** A target amount for one payee on one plan over a period.

**Configuration rules:**
- The period is a free start/end date range and **must fall entirely within the plan's period**.
  Partial overlap is rejected.
- Measurement is **Revenue** or **Units** (`QuotaMeasurementType`, Revenue/Units exposed;
  Margin/ACV/Bookings exist in the enum but are not selectable).
- Currency is locked to the plan's currency.

> Note: `QuotaMeasurementType` and the rule's `MeasurementType` are **different enums with different
> numeric values**. Don't conflate them.

**Lifecycle: Draft → Active → Closed.** Strictly linear — no reopening, no un-closing
(`Quota.cs:106-132`).

**Status is never derived from dates.** Only the explicit *Activate* and *Close* actions change it.
A quota whose period has ended stays Active until someone closes it. Attainment queries include
every non-Draft quota, so Draft quotas contribute nothing.

**Quotas are immutable after Draft.** `UpdateDraft` throws for any non-Draft status
(`Quota.cs:84-87`). The UI states this up front: *"Quotas cannot be modified after creation."*
To change a target, close the quota and create a new one.

**On screen.** The quota detail page shows a status badge, employee code, period chip and amount
chip; a field grid with Payee, Status, Period, Target amount, Created at, Plan. **Activate** appears
only for Draft; **Close Quota** only for Active; neither once Closed. There is no edit control
anywhere, matching the Draft-only guard.

**Overlapping quotas.** Nothing prevents several quotas covering the same date. If more than one
matches, the engine picks the **narrowest** period, then the most recently created
(`QuotaAttainmentService.cs:61-65`). Monthly quotas sitting inside a quarterly plan work this way —
but it's a usage convention, not a modelled parent/child relationship, and nothing validates that
they tile the period without gaps.

---

## 9. Transactions

**What they represent.** Closed-won sales. HubSpot enforces this — the sync filters on HubSpot's
calculated `hs_is_closed_won` property (`HubSpotCrmDealSource.cs:19` class doc, `:479` search filter).
**Excel import and manual entry do not filter**: whatever you provide is accepted. The UI states this as
an expectation, not a rule.

**Three sources:**

| Source | Payee resolution | Closed-won enforced |
|---|---|---|
| HubSpot sync | deal owner → payee, by stored mapping then email match | Yes |
| Excel import | payee code column | No |
| Manual entry | payee picker | No |

**Unassigned transactions.** A HubSpot deal whose owner matches no payee still imports, with no
payee — the sync is never blocked and no payee is ever auto-created. Excel import behaves the same
way when *Require payee on new transactions* is Optional. These appear in the dashboard's
**"Transactions that need attention"** card under **No payee assigned**, defined as
`Status == Pending && PayeeId == null`.

**Lifecycle: Pending → Calculated → Paid.**

| Transition | Trigger |
|---|---|
| → Pending | ingested from any source |
| Pending → Calculated | credits allocated |
| Calculated → Paid | the payout containing it is marked paid |
| Calculated → Pending | credits recalculated, or the payee is (re)assigned |
| Pending → Cancelled | voided (requires a reason) |
| Calculated → Cancelled | its CRM deal was **lost** and the admin reverted the commission (deal-lost only; credits superseded, not deleted) |

A **Paid** transaction cannot be reverted to Pending, and that has not changed. What changed is what
happens instead: ✅ **an already-paid commission is now corrected by the clawback ledger** — a debt is
recorded against the payee and collected from future pay runs. The transaction and its credits are
never rewritten. See [section 15](#15-clawback). Only a **Calculated** (never-paid) commission can be
*reverted*; a **Paid** one is *clawed back*, and those are deliberately different operations run by
different commands.

*(Superseded: earlier versions of this guide said the correction workflow "does not exist yet". It
exists as of 2026-07-29 and is verified end to end.)*

**CRM deal lifecycle (won → lost → won).** Because HubSpot is the source of truth for the sale:
- A credited deal that turns **Closed Lost** is detected on the next sync (reverse reconciliation,
  `DealLostReconciler`). If its commission is only **Calculated**, the dashboard offers **Revert
  commission** (supersedes the credit, cancels the transaction). If it was already **Paid**, it is shown
  for information only *on that card* — there is no revert button, because reverting a payment that
  already left the company would be a lie. ✅ Instead the **churn clawback trigger** fires
  automatically for Paid commissions and records the proportional debt
  ([section 15](#15-clawback)); the alert row then says whether the clawback has been applied or is
  still pending, read from the ledger.
- A deal that comes **back to Closed Won** after being deal-lost-cancelled is **re-credited
  automatically**: a fresh transaction is created (`TransactionCreateGuard` recognises the deal-lost
  cancellation and re-opens it), the old cancelled row stays as history. This never re-credits a Paid
  commission — a deal-lost cancellation was never paid by construction (anti-double-pay guard).

---

## 10. Measurement period vs payment period

> ✅ **They are fully independent.** A pay run has its own start/end dates, chosen freely. There is
> no foreign key, navigation property, or shared validation between `PayRun` and `Quota` — the word
> "Quota" does not appear in the pay-run handlers at all. The only validation on a pay run period
> anywhere in the system is `start <= end`.

So measuring quarterly and paying monthly **works today**. Be aware *why* it works: the two concepts
are decoupled because nothing connects them, not because the relationship is modelled. No screen,
validation or report expresses "this run covers part of that measurement period."

**What a pay run actually gathers:** transactions whose `TransactionDate` falls inside
(**pay run period ∩ assignment period**) — never the quota period
(`CalculatePayoutsForPeriodHandler.cs:144-152`).

**How attainment behaves when they differ.** Attainment is consumed earlier, at credit-allocation
time, against the quota's own period. A monthly pay run over a quarterly quota simply pays that
month's credits — and those credits already carry rates computed against quarter-to-date attainment.
The pay run does not recalculate attainment.

> 📚 **Draws, true-ups and pay-period reconciliation — NOT implemented.** Measuring on one cadence
> and paying on another usually comes with recoverable/non-recoverable draws and a true-up at
> period end. Wasnie has none of these. There is no advance, no recovery, no reconciliation step.

> 📚 **Named frequency (monthly / quarterly / annual) — effectively NOT implemented.** A
> `PlanPeriodType` enum exists on Plan, but it is optional, read by nothing, exposed by no DTO or
> screen, and consumed by no calculation. Periods are free-form dates everywhere. Quotas and
> assignments have no frequency field at all.

---

## 11. Pay runs and payouts

**What a pay run is.** The act of closing one period for everyone at once. It is **tenant-wide**:
every active assignment for every payee across every plan. The Calculate dialog has a Plan field,
but it only pre-fills the dates — it is not sent to the API and does not filter the run.

**It recalculates payouts, but not credits.** Existing `Calculated` payouts for the period are
deleted and rebuilt from current credits. Credits themselves were computed earlier, at
transaction-processing time. To regenerate credits (e.g. after changing SplitAtQuota), use
**Recalculate credits** on a Draft run — a separate, explicit operation.

**Pay run lifecycle: Draft → Approved → Paid.**
- **Approve** — only from Draft. Cascades: each `Calculated` payout is approved (Disputed ones are skipped).
- **Mark as paid** — only from Approved. **Irreversible; the period is locked.** Cascades to payouts, marks credits consumed, and marks transactions Paid.
- **Reopen** — only from Approved, never from Paid. Always run-wide; partial reopen is not allowed.

**Payout lifecycle: Calculated → Approved → Paid**, plus **Disputed** (reachable from Calculated or
Approved, but not from Paid, and it is a dead end — nothing transitions out of Disputed).

**Supplemental runs.** Calculating a period that already has runs behaves three ways: reuse an
existing Draft; create the primary if none exist; otherwise create a **supplemental** run
(sequence 1, 2, …). Supplementals are safe because payees already paid are protected by the
anti-double-pay guards, so a supplemental only picks up payees and plans absent from earlier runs.

**Anti-double-pay.** Before marking a run paid, the system checks for credits already consumed by
another payout. If any are found the whole run is **blocked** with an audit entry and a list of the
conflicting transaction references and the payouts that already paid them — rather than failing
part-way through.

> ⚠️ **Overlapping pay runs are possible.** Uniqueness is on *exact* period dates plus sequence, so
> Jan 1–31 and Jan 15–Feb 15 can both exist. An overlaps query exists but is advisory — it is
> exposed on a detail endpoint and is never consulted when creating or calculating. The real
> protection against paying the overlap twice is the credit-consumption guard, not a period
> constraint.

**On screen.**
- **Pay Runs list** — columns Period, Status, Payees (*"{paid} paid · {total} total"*), Totals (one line per currency), Created. A **Supplemental** badge appears alongside the status for sequence > 0. **Remove Draft** shows only for Draft runs.
- **Pay Run detail** — actions Remove Draft / Recalculate credits / Approve run / Reopen run / Mark run as paid, shown per status. Summary of Period, Status, Payees, Totals, Created, Approved, Paid. A payouts table (Payee, Plan, Period, Total, Status) with a **"Hiding $0 payouts"** toggle — €0 payouts are normal and expected for assigned payees with no sales.
- **Payout detail ("Payout Statement")** — Summary (Payee, Plan, Period, Status, Total Commission) and **Commission Lines**: Rule, Source Transaction, Base Amount, Commission, with a total row. Expanding a line shows the plan version, the rate, the trigger conditions (or *"Applies to all transactions"*), and the adjustment chain. An Audit Trail section shows who calculated it and when.

---

## 12. End-to-end: one deal, all the way through

Plan **EU Accelerator Q2 2026** (Apr 1 – Jun 30, EUR), one attainment rule, tiers 4% / 7% at the
quota boundary, **SplitAtQuota ON**. Payee Adrian, quota **€250,000** for the same period.
By the time this deal lands, Adrian has already been credited **€250,000** of revenue.

**1. Transaction arrives** — €27,880.25, dated within Q2, payee resolved. Status **Pending**.

**2. Credit allocation.** The rule's trigger matches. Measurement is Revenue, so the base amount is
the transaction amount. The rate table is attainment-based with SplitAtQuota, so the engine loads
the split context: prior cumulative €250,000, quota target €250,000.

Tier bands in absolute money: 4% covers €0–250,000; 7% covers €250,000 and up. This deal occupies
€250,000 → €277,880.25, which lies entirely in the 7% band:

```
27,880.25 × 7% = 1,951.6175
```

A **Credit** is written with `OriginalAmount` = €27,880.25, `CreditedAmount` = €1,951.6175, plus a
frozen snapshot of the rule (name, rate table, trigger, plan version) — that snapshot is what lets
the statement explain the number months later. The transaction moves to **Calculated**.

*(Had this been the payee's first deal of the quarter, the split would have been
€250,000 × 4% + €27,880.25 × 7% = €11,951.6175 — the full-quarter figure quoted in section 6.)*

**3. Pay run calculated** for a period covering the transaction date. The run finds every active
assignment, intersects its period with the run's, collects transactions in that window, then pulls
their credits — skipping any superseded or already-consumed. One **PayoutLine** per credit carries
`BaseAmount` €27,880.25 and `CommissionAmount` €1,951.6175, referencing the credit and the rule.

> Note: a payout line references the **credit**, not the transaction. The transaction reference
> shown on the statement is resolved at read time via the credit.

**4. Payout.** Lines are summed into one **Payout** per (payee, plan, period), status
**Calculated**, assigned to the run.

**5. Approve → Mark paid.** Approving cascades to the payouts. Marking paid cascades to payouts,
stamps each credit as **consumed** (so it can never be paid again), and marks the transactions
**Paid**. The period is locked.

**Amount carried across each hop:**

```
Transaction.Amount  →  Credit.OriginalAmount   →  PayoutLine.BaseAmount
computed commission →  Credit.CreditedAmount   →  PayoutLine.CommissionAmount
                                                →  Payout.TotalCommission (sum)
                                                →  PayRun.TotalAmounts (per currency)
```

---

## 13. Industry concepts NOT implemented

These come up in sales-comp conversations. None exists in Wasnie today. Listed so nobody
accidentally presents them as features.

| 📚 Concept | Status |
|---|---|
| **Draws** (recoverable / non-recoverable) | Not implemented. No advance, no recovery. |
| **True-ups** | Not implemented. No reconciliation between a measurement period and its pay periods. |
| **Splits / overlays (1:N)** | Schema-only. `Credit` carries `SplitPercentage` and `Role` (Primary/Overlay/Split), but the allocator always writes **Primary at 100%**. No UI, no multi-payee split. |
| **SPIFFs as distinct behaviour** | Name only. `Spiff` is a `ModifierType` value, but it multiplies by `Factor` exactly like the others. |
| **Windfall / materiality thresholds** | Not implemented. No outsized-deal detection or capping by threshold. |
| **Named frequency** (monthly/quarterly as a formal concept) | Not implemented. `PlanPeriodType` exists but is inert metadata. |
| **Weighted averages** | Not implemented anywhere. |
| **Second KPI unlocking a rate** (e.g. margin gate on a revenue rate) | Not implemented. One measurement per rule (a rule's Trigger can filter on eight fields, but that gates whether the rule fires — it does not blend a second KPI into the rate). |
| ~~**Clawback of a *paid* commission**~~ | ✅ **NOW IMPLEMENTED** (2026-07-28/29) — moved out of this table. Proportional churn clawback, an append-only payee ledger with a negative-balance model, and deduction from future pay runs bounded by a per-plan cap. See [section 15](#15-clawback). |
| **Period-scoped caps** | Not offered in the UI and rejected by the API — only per-transaction caps apply (the backend enum keeps *Per period* / *Total* for future use). See 4.4. |
| **Tier accumulation across a period (Tiered tables)** | Tiered restarts per transaction. Period accumulation exists only via attainment + SplitAtQuota. |

---

## 14. Behaviours worth knowing

Collected from the verification passes — real behaviours, not bugs to work around silently.

1. **Caps are always per-transaction** — the only scope offered; other scopes are rejected by the API.
2. **Trigger conditions use a field dropdown from the engine's catalog** (eight fields) with operators derived from each field's type — `In` / `NotIn` for string fields, ordering operators for number/date — and are validated server-side at save. (This replaced the earlier free-text/`Equal`-only limitation.)
3. **`Measurement` aggregation and source field are inert.**
4. **Tiered tables do not accumulate across transactions.**
5. **Attainment sums the whole quota period**, so backdated or out-of-order ingestion shifts figures for already-processed transactions.
6. **A quota's status never changes by itself** — an expired quota stays Active until closed.
7. **Quotas cannot be edited after Draft** — close and recreate.
8. **SplitAtQuota ON with no quota pays zero**, silently to the user (logged server-side).
9. **Pay runs are tenant-wide**; the plan selector only pre-fills dates.
10. **Overlapping pay runs are possible**; only credit consumption prevents double payment.
11. **€0 payouts are correct**, not errors — assigned payees with no sales in the period.
12. **Changing a rule does not retroactively change existing credits.** Use *Recalculate credits* on a Draft run.
13. **Archiving a plan deactivates its assignments** — it drops out of processing and the pending-eligible lists.
14. **A payee can be in several active plans**; each transaction credits **every** applicable plan (one credit per plan). When 2+ plans are eligible and none is stated, manual entry makes the admin pick and Excel/HubSpot fail loud (left uncredited, flagged) rather than guessing.
15. **The clawback is opt-in per plan and born inert** — no maturation window, no clawback. Setting one is a deliberate act; a cloned plan version inherits it. (15.1)
16. **A clawback never edits the payment it corrects.** It writes a debt, collected from future runs; the transaction, its credits and the payout stay untouched. (15)
17. **A payee balance can go negative without limit**, and the cap only limits how much a single run may withhold — the rest carries over. (15.3)
18. **Terminating a payee freezes the account but does not cancel a payout already calculated** — it is paid and still nets against the debt. (16.2)
19. **A final settlement must equal the balance exactly** — over *or* under is rejected. Write-offs and external settlements may be partial. (16.3)
20. **The live balance and the pay run's carryover are different numbers** and legitimately differ; the screen explains the gap instead of hiding it. (17.2)
21. **A Rep can read their own ledger**, by design. Nobody but TenantAdmin/CompManager can write one. (18)

---

## 15. Clawback

**What it is.** The way Wasnie corrects a commission that has **already been paid**. It never edits
the original payment: it records a **debt** against the payee and collects that debt from future
pay runs. Everything is append-only — the transaction, its credits and the payout that paid it are
left exactly as they were, because rewriting the history of a payment that really happened is the
one thing a ledger must not do.

The pieces:

```
Deal lost in the CRM, commission already Paid
        │
        ▼
ClawbackDebit  (Origin = System)  ──►  PayeeLedgerEntry (append-only)
        │                                      │
        │                                      ▼
        │                               PayeeBalance   (one per payee + currency)
        ▼                                      │
next pay run marked Paid  ──►  withholds up to each plan's cap  ──►  ClawbackAppliedCredit
```

### 15.1 Configuration: the policy is opt-in per plan

*Plans → open a plan → **Clawback** tab.* Two fields, both nullable (`Plan.cs:33,40`):

| Field | Meaning | Empty means |
|---|---|---|
| **Maturation days** | How long a deal must stay won before the commission is fully earned | This plan **never** claws back churned deals |
| **Cap %** (0–100) | Most of a period's commissions this plan lets a clawback withhold | No ceiling — the whole payout may be withheld |

> ✅ **The subsystem is born inert.** Both fields are null on every pre-existing plan, so nothing
> changes for anyone until a tenant deliberately sets a maturation window
> (`RegisterDealChurnClawbackHandler.cs:131-137`: a credit whose plan has no window is skipped and
> the outcome is reported as *no policy*, which is a configuration state, not a failure).

> ✅ **A renewed plan inherits the policy.** `CloneAsNewVersion` copies both fields — a new version
> silently dropping the clawback would have turned a renewal into an amnesty.

**Validations:** maturation days must be > 0; cap must be between 0 and 100; an **archived** plan
rejects the change outright (`Plan.cs:150-157`).

### 15.2 The formula

```
clawback = commissionPaid × (maturationDays − daysActive) ÷ maturationDays      floored at 0
```

`ClawbackCalculator.cs:28-45`. Two properties are deliberate and worth stating to a customer:

- **One multiplication, then one division.** Computing the ratio first (`1 − 30/90 = 0.6666…`)
  rounds before it multiplies and loses cents on large commissions. This form keeps
  `900 × 60 ÷ 90` at exactly `600.00`.
- **Floored at zero.** A deal that outlived its window gives back **nothing** — a clawback can never
  turn into a bonus (`:41-42`).

`daysActive` runs from the transaction (close-won) date to the CRM loss date, floored at 0: a loss
dated *before* the close is bad CRM data, not a negative lifetime, and is treated as 0 days active
— i.e. the full clawback (`ClawbackCalculator.cs:66-70`).

**Origin error** (the contract was never real) uses `Full` — 100%, not proportional
(`ClawbackCalculator.cs:51-59`). There is no time earned on a sale that never existed.

> ⚠️ Today only the **churn** trigger is wired to a live event (deal lost in HubSpot). The
> origin-error method exists and is tested but has no automatic trigger; the equivalent correction is
> made by hand as a `DataCorrectionDebit` ([17.4](#174-manual-adjustments)).

### 15.3 Use cases

**Happy path — a deal churns inside the window.**
Plan with **maturation 180 days**, cap 50%. A deal closed on 1 Feb paid a commission of **€1,000**.
HubSpot reports it lost on **1 May** → 89 days active.

```
1,000 × (180 − 89) ÷ 180 = 505.5556  →  ClawbackDebit −€505.5556, balance −€505.5556
```

The payee's next pay run pays €2,000 of commission with a 50% cap:

```
ceiling  = 2,000 × 50%  = 1,000.00
withheld = min(505.5556, 1,000.00) = 505.5556
net paid = 2,000 − 505.5556 = €1,494.4444        balance → 0, carryover 0
```

**Edge — the deal outlived its window.** Same plan, deal lost after **200** days: `180 − 200 < 0`
→ nothing is written at all, and the outcome is reported as *matured*
(`RegisterDealChurnClawbackHandler.cs:156-157,195-197`). No zero-value entry pollutes the ledger.

**Edge — the cap does not let the debt be collected in full.** Debt **€900**, payout **€1,000**,
plan cap **50%**: the ceiling is €500, so €500 is withheld and **€400 carries over** as debt
(`PayeeSettlementCalculator.cs:62-77`). The payee always takes home at least (100 − cap)% of what
they earned. The statement says so explicitly ([17.1](#171-the-two-equations)).

**Edge — the payee owes more than they will ever earn.** The balance simply goes further negative;
there is **no floor at zero** (`RegisterDealChurnClawbackHandler.cs:37-40`). A balance of +€100 hit
by a clawback of €988.8889 becomes **−€888.8889**, which carries over and nets against later
commissions. A floor would reward timing a churn against an empty account.

**Edge — one transaction credited under two plans.** One entry is written **per (plan, currency)**
(`:150`), because maturation is a plan setting: two plans mean two different windows, and collapsing
them would produce a row nobody can explain. Both debits land on the **same** balance — the debt is
global per (payee, currency), only the *cap* is per plan.

**Edge — the same lost deal is seen on every sync.** Idempotent: a transaction that already has a
churn debit is a no-op (`:88-98`), backed by a unique filtered index on
(`SourceTransactionId`, `SourcePlanId`) so the guard holds even against a race a read-then-write
check cannot see.

**Edge — the deal was lost in March but nobody synced until July.** The **event date** drives the
formula; the entry is **booked in the currently open period** (`:173`). A closed, already-paid run is
never reopened to receive a retroactive debit.

**Edge — the sync fires while finance is closing a pay run.** `PayeeBalance.RowVersion` is a real SQL
rowversion. On conflict the handler re-reads the balance and re-applies its entries on top of the
other writer's figure, up to 3 attempts (`:282-316`). The outcome is always *"the debit made it into
the run"* or *"the debit waits for the next run"* — never lost, never doubled.

**Edge — the commission was calculated but never paid.** No debt is created: the handler only counts
credits that are **consumed by a paid payout** (`:103-119`), and it refuses outright if the
transaction is not `Paid` (`:79-82`). Money that never left the company is corrected by *reverting*
the commission, not by inventing a debt.

**Edge — the CRM gave no loss date.** Nothing is generated; the deal-lost alert stays open for a
human. Inventing a date would charge the salesperson for Wasnie's own sync latency.

### 15.4 Netting inside a pay run

Settlement runs at **Mark as paid**, not at calculation (`PayRunSettlementService.cs:30-41`), for two
reasons that both cost money if ignored: calculation deletes and rebuilds Calculated payouts on every
re-run (a ledger write there would duplicate), and a payout that is calculated but never paid must
not reduce anyone's debt.

- The debt is **global per (payee, currency)**; the **cap is per plan**. A payee with two plans has
  their single debt collected from both payouts, each limited by its own plan's ceiling
  (`PayeeSettlementCalculator.cs:7-13`).
- Withholding order is deterministic — by plan id, then payout id (`:62`) — so two runs over the same
  data always withhold from the same payouts in the same sequence.
- Cross-currency is refused, not converted: Wasnie holds no exchange rates (`:46-49`).
- Everything the settlement writes lands in the **same `SaveChanges`** as `Credit.Consume()`. That
  atomicity is what stops a credit being consumed while its settlement is lost, or the reverse.

### 15.5 Validations and errors

| Situation | What the system does |
|---|---|
| Transaction is not `Paid` | Refused: *"a churn clawback applies to a PAID commission… an unpaid commission is corrected by reverting it"* |
| Transaction has no payee | Refused: there is no ledger to charge |
| Plan has no maturation window | No entry; outcome *no policy* (inert, not an error) |
| Deal outlived the window | No entry; outcome *matured* |
| Nothing was actually paid | No entry; outcome *nothing paid* |
| Already clawed back | No-op; outcome *already posted* |
| Maturation ≤ 0 / cap outside 0–100 | Rejected at the plan, before any calculation |
| Entry currency ≠ balance currency | `DomainException` — balances are per currency |

### 15.6 Permissions

The churn trigger is **System**: no human can invoke it over HTTP — there is no endpoint, it fires
from the CRM sync. Reading the resulting ledger needs `Ledger.Read`; writing a manual entry needs
`Ledger.Adjust` ([section 18](#18-permissions-and-roles)).

---

## 16. Termination and orphaned accounts

**What it is.** What happens to money when a payee **leaves**. Terminating someone freezes their
account: they earn nothing more, and their balance stops moving on its own. That is correct — and on
its own it would also make a debt invisible, which is how debt quietly disappears. So the freeze
ships with a work queue and three explicit ways to close the account.

### 16.1 Configuration

*Payees → open a payee → Terminate*, with a termination date. Nothing else to configure: the
behaviour below is automatic.

### 16.2 Use cases

**Happy path — a terminated payee leaves the engine.** From the next calculation on, every
assignment belonging to a terminated payee is dropped before the payout loop
(`CalculatePayoutsForPeriodHandler.cs:76-96`), with a log line naming how many were skipped. If that
leaves nothing, the run returns zero payouts rather than failing.

> ✅ **The switch lives on the `Payee` aggregate, not on the ledger.** A mutable "frozen" flag on the
> ledger would have broken the append-only model the whole subsystem rests on. The debt stays exactly
> where it was, visible in `PayeeBalance` and in the queue below.

**★ Edge — a payout already calculated is NOT cancelled.** Terminating someone does not destroy pay
they earned while working. The filter only stops **new** payouts being generated; an existing
residual payout is still paid and **still nets against their debt** at settlement — which is the last
real chance to recover it.

**Happy path — the queue.** *Financials → Terminated accounts* (`/terminated-accounts`), backed by
`GET /api/payees/ledger/terminated-with-balance`. It lists every terminated payee whose balance is
**≠ 0**, deepest debt first (`ListTerminatedPayeesWithBalanceHandler.cs:37-50`).

> ✅ **A positive balance appears too.** Money Wasnie still owes someone who has left is exactly as
> unfinished as money they owe. Hiding it would be the same mistake in the other direction.

> ✅ **One row per (payee, currency).** Someone owing EUR and owed USD legitimately appears twice —
> Wasnie holds no exchange rates and must never show a single blended figure.

**Edge — the dashboard says so first.** The count also appears in *Requires action* on the dashboard,
split into **To pay** (positive balances) and **To recover** (negative), linking to the same screen.
A single number would hide which kind of work is waiting: paying someone and collecting from someone
are not the same task.

### 16.3 Closing an account — three types, and they are not interchangeable

All three are written through the ordinary manual-adjustment flow on the payee's ledger; there is
deliberately no second write path. The UI offers only the ones that make sense for the sign of the
balance (`payee-ledger-panel.component.ts:78-91`) — offering both directions would let someone "write
off" money the company **owes**, which is not a write-off, it is not paying somebody.

| Balance | Type | Meaning | Amount rule |
|---|---|---|---|
| Negative (they owe) | **ExternalSettlementCredit** | Recovered outside Wasnie — typically deducted from the final paycheck by payroll | **Partial allowed** |
| Negative (they owe) | **WriteOffCredit** | The company absorbed the loss; the debt is uncollectable | **Partial allowed** |
| Positive (we owe) | **FinalSettlementDebit** | Treasury paid the departed payee what they were owed, outside Wasnie | **Must equal the balance exactly** |

Two credits and not one generic "closing credit", because *"how much we recovered through HR"* and
*"how much we ate"* are different facts about the business, and a CFO must be able to total each
without mining free text.

> ★ **`FinalSettlementDebit` requires strict equality** (`PayeeBalance.cs:84-113`). It exists to
> **extinguish** the account so it leaves the queue, so:
> - the balance must be **positive** — against zero there is nothing to settle, and against a
>   negative one the entry would sink the debt deeper under a label claiming the account closed
>   (`FinalSettlementRequiresPositiveBalance`);
> - the amount must **equal** the balance — a partial payment leaves a positive remainder, the
>   account is still orphaned, and the entry has not done the one job its name claims
>   (`FinalSettlementMustEqualBalance`).
>
> Wasnie does not orchestrate instalments; that is an ERP's accounts payable. **A closing is total, or
> it is not a closing.** Because the amount is typed by a person, the form fills it in from the live
> balance and **locks the field** — but the guarantee is the domain rule, not the read-only input: the
> API rejects a wrong amount with **400** whatever the browser did.

**Worked example.** A departed payee ends **+€500** (a pay run withheld more than they actually
owed, later corrected):

- €500 `FinalSettlementDebit` → balance **0.0000**, the row leaves the queue. ✅
- €600 → **400**, `FinalSettlementMustEqualBalance`, nothing written, balance still +€500. A typo
  would otherwise have flipped the balance to −€100 and invented a debt against someone who has
  already left — which then reappears in this very queue asking for a write-off to "fix" it.
- €300 → **400**, same code: partial closings are refused.

And a departed payee at **−€250**: a €250 `WriteOffCredit` (or `ExternalSettlementCredit`) takes them
to zero; **€150 is also accepted**, leaving −€100 still owed. The form pre-fills the debt but leaves
the field editable, and says so.

### 16.4 Validations and errors

| Situation | Result |
|---|---|
| `FinalSettlementDebit` against balance ≤ 0 | 400 — `FinalSettlementRequiresPositiveBalance` |
| `FinalSettlementDebit` ≠ balance (over **or** under) | 400 — `FinalSettlementMustEqualBalance` |
| Closing entry with no justification or no actor | 400 — an entry nobody signed is not a decision |
| A Rep tries to close an account | **403** — hidden in the UI and refused by the API |
| Closing type that contradicts the sign of the balance | Not offered in the UI; the domain still governs the outcome |

**Nothing is ever deleted.** A closing entry sits *next to* the debit that created the debt; both
stay visible and the ledger sums to zero.

### 16.5 Where Wasnie stops

Wasnie **freezes and records**; it does not collect. Deducting from a final paycheck, sending to
collections or pursuing legally happens in HR / finance / legal with data Wasnie does not hold. The
app's job is to make the open account impossible to overlook and to store the decision finance made.

### 16.6 Permissions

Seeing the queue: `Ledger.Read`. Closing an account: `Ledger.Adjust`. The **Close account** button is
**hidden** — not disabled — from anyone without it.

---

## 17. Ledger and statement

**What it is.** The payee-facing face of the clawback subsystem: *Payees → open a payee →
**Clawback** tab*. Two equations, the live balance, and every entry that ever moved it.

### 17.1 The two equations

**Cash flow of the settled run** (absolute values; the sign lives in the operator):

```
Commissions − Withheld = Takes home this month
```

**Balance movement** (explicit signs, because the contrast is the lesson):

```
Previous balance + Paid down = Carryover at that run
```

Both describe **one past payment**. Every figure is computed server-side from `PayRunSettlement` and
nothing is recalculated in the browser — if the settlement says €500 was withheld, €500 is what left.

> ⚠️ **`Cap %` may be blank on purpose.** When the plans that paid the payee in that run have
> **different** caps there is no single percentage to name, so the field is null and the caption
> switches to a wording that does not quote one. Inventing a number in a sentence that explains
> someone's salary would be a lie.

### 17.2 Live balance vs the photograph — the distinction that caused a real bug

| Figure | What it is | When it changes |
|---|---|---|
| **Current balance** | The sum of the payee's **whole ledger**, right now (`PayeeBalance`) | Every entry, including ones added after the last run |
| **Carryover at that run** | What was left **at the close of that payment** | Never — it is history |

They are separate fields in the DTO (`PayeeStatementDto.cs:37,43-46`) and the screen leads with the
live one. **When they differ the screen says why**: *"There have been movements after this pay run
(−€333.33), which is why the current balance is −€833.33."*

> ⚠️ Everything belonging to the settled run is **nullable and shown as an em dash when absent** —
> never as `0`. A zero would claim "this person earned nothing and took nothing home" when the truth
> is "no pay run has closed against this balance yet".

*(Corrected in this document: earlier versions predate this split, when one overloaded field meant
both things — which is exactly how a screen came to show −€500 while the ledger below summed to
−€833.33.)*

### 17.3 The ledger table

Append-only, newest first, with **three redundant signals** for System vs Human (icon, colour band,
and the sign/colour of the amount — colour alone is not a distinction a colour-blind CFO can use).

| Column | Notes |
|---|---|
| Source | **System** (the engine wrote it) or **Manual** (a person did, with actor and justification) |
| Date | When Wasnie **booked** the entry |
| Deal lost on | When the deal actually died in the CRM — a **typed** field, not a phrase inside the justification; em dash when no CRM event caused the entry |
| Type | See the table below |
| Detail | The justification, plus the author for manual entries |
| Amount | Signed: negative reduces what the payee is owed |

**The nine types** (`LedgerEnums.cs`). The **sign is derived from the type**, so a debit stored as a
positive amount is unrepresentable:

| Type | Sign | Who may write it |
|---|---|---|
| `ClawbackDebit` | − | **Engine only** — a person must never hand-write a clawback |
| `ClawbackAppliedCredit` | + | **Engine only** — written by the pay run that actually withheld |
| `ClawbackForgivenessCredit` | + | Human — a **business** decision to let a real debt go |
| `ManualBonusCredit` | + | Human |
| `DataCorrectionDebit` | − | Human — bad data inflated a payment |
| `DataCorrectionCredit` | + | Human — neutralising an entry a **technical** fault produced |
| `ExternalSettlementCredit` | + | Human — debt recovered outside Wasnie |
| `WriteOffCredit` | + | Human — the company absorbed the loss |
| `FinalSettlementDebit` | − | Human — cash paid to a departed payee |

> ✅ **`DataCorrectionCredit` is not `ClawbackForgivenessCredit`, and the difference is not
> cosmetic.** Forgiveness is a business decision: someone with authority let a **real** debt go.
> Using it to erase a bad import would tell the CFO the company forgave money it never charged, and
> no amount of free-text justification recovers that distinction once the totals are added up.

### 17.4 Manual adjustments

*Add adjustment* on the ledger tab → type, amount, justification. The type list follows the sign of
the balance for the closing types ([16.3](#163-closing-an-account--three-types-and-they-are-not-interchangeable));
the four general corrections are always available.

- The **actor** comes from the authenticated user, never from the request body — an adjustment whose
  owner the caller can choose is not an audit trail.
- The **payee** comes from the URL, not the body — the entry lands on the resource the caller was
  authorised against.
- **Justification is mandatory** and enforced in the domain, not just the form.
- The engine-only types (`ClawbackDebit`, `ClawbackAppliedCredit`) are **rejected** over HTTP.
- Everything is **append-only**: an adjustment adds an entry, it never edits or deletes one. A
  mistake is corrected by a counter-entry, and both rows stay.
- After saving, the screen **re-reads from the server** instead of patching the balance locally — the
  balance moved server-side and re-reading is the only way the screen and the ledger cannot disagree.

### 17.5 Validations and errors

| Situation | Result |
|---|---|
| Empty justification | 400, nothing written |
| Engine-only type | 400 |
| Amount ≤ 0 | Rejected — the magnitude is always positive; the **type** carries the sign |
| Unknown type name | 400, *"Unknown adjustment type"* |
| Anonymous request | 401 |
| Rep or Manager posting an adjustment | 403 |
| Entry currency ≠ balance currency | 400 — balances are per currency, no FX |
| Adjustment landing while a pay run settles | The pay run's write fails on the rowversion instead of overwriting a stale balance |

### 17.6 Permissions

`Ledger.Read` to see the statement and the entries — **including the Rep, for their own account**:
seeing *why* a payment shrank is the point of the feature. `Ledger.Adjust` to write.

---

## 18. Permissions and roles

Four roles, mapped to explicit permissions (`RolePermissions.cs`). The UI follows one rule
throughout: **forbidden actions are hidden, never shown-and-disabled** — the screen shows what you
can do, and the API enforces it regardless of what the browser rendered.

| Role | Scope |
|---|---|
| **TenantAdmin** | Everything, plus subscription, settings and integrations |
| **CompManager** | Everything operational: payees, plans, quotas, assignments, transactions, credits, payouts, ledger, imports, reports — no subscription/settings/integrations |
| **Manager** | Read-only: payees, quotas, assignments, **and the ledger** |
| **Rep** | Read-only: payees, assignments, quotas, **and the ledger** |

**The two ledger permissions:**

| Permission | Who has it | What it opens |
|---|---|---|
| `Ledger.Read` | TenantAdmin, CompManager, **Manager**, **Rep** | The clawback tab (statement + entries) and the terminated-accounts queue |
| `Ledger.Adjust` | TenantAdmin, CompManager | The **Add adjustment** form and every closing entry |

> ✅ **Why a Rep can read the ledger.** Transparency is the differentiator: the rep sees their own
> balance and why it moved. A deduction the person cannot examine is how trust in a comp system dies.
> **Why a Manager can.** They have to be able to explain a reduced payment to their rep
> (`RolePermissions.cs:56-57,66-67`).

> ⚠️ **`Ledger.Read` is not row-scoped.** Anyone holding it can read any payee's ledger in their
> tenant; there is no "own records only" filter today. Tenant isolation is enforced (global query
> filters), payee-level scoping is not.

**Verified behaviours:** a Rep gets **403** on `POST /ledger/adjustments` and **200** on
`GET /ledger/statement`; a Manager gets 403 on the adjustment; an anonymous request gets **401**
(`LedgerEndpointsTests.cs`).

---

## Maintenance

When the calculation engine, rate tables, quota/payout lifecycles, the pay-run aggregation or the
clawback ledger change, **re-verify this document against the code and update the verification date
at the top.** The sections most likely to drift are 5, 6, 7, 11 and 15. Prefer deleting a claim over
leaving one that may have become false.

**This guide is written in passes.** Update the [coverage checklist](#coverage-checklist-this-document-is-written-in-passes)
in the same commit as the section you add, and leave partially covered areas marked `[~]` with a note
saying what is still missing — an area silently marked done is worse than one openly marked pending.
