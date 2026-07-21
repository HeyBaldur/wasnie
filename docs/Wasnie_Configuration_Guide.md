# Wasnie — Configuration Guide

**What this is:** a walkthrough of how Wasnie is configured and how it calculates, with worked
numbers and what appears on screen at each step.

**Who it's for:** two readers at once —
- **Operators / owner** — a reference for how the system actually behaves.
- **New customers** — a first-use guide, in the order you'd really do it.

**Verified against the code on 2026-07-21.** Every statement about Wasnie's behaviour in this
document was checked by reading the source, not from memory, and cites `file:line` so you can
re-check it. **If the calculation engine changes, this document must be re-verified** — a guide that
misdescribes the engine is worse than no guide. The highest-risk sections are
[Rate tables](#5-rate-tables), [SplitAtQuota](#6-splitatquota--the-accelerator-question) and
[Attainment](#7-attainment).

**Related docs:** `Pay_Run_Model.md` (why pay runs exist — design rationale),
`Wasnie_Product_Master_Specification.md` (product scope), `ARCHITECTURE.md` (engineering rules).
This guide describes **as-built behaviour**; where it disagrees with the spec, this document
reflects what the code does and says so explicitly.

---

## How to read this document

Two kinds of content appear here, and they are never mixed:

> ✅ **In Wasnie** — implemented and verified. Safe to rely on and to demo.

> 📚 **Industry concept — NOT implemented in Wasnie.** Context only. Do not present as a feature.

Everything is ✅ unless explicitly marked 📚. A consolidated list of the 📚 items is in
[section 12](#12-industry-concepts-not-implemented).

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

Either **all transactions**, or a set of conditions combined with **And** / **Or**.

> ⚠️ **Important limitation.** The condition **Field** is a free-text input in the UI, but the
> engine resolves exactly three names (`CommissionCalculator.cs:46-52`):
>
> - `transactionAmount`
> - `transactionDate`
> - `source`
>
> Any other field name logs a server-side warning and the condition evaluates to **false** — the
> rule silently never fires. There is no dropdown and no validation preventing this. Use only those
> three names.
>
> Also: `In` / `NotIn` operators can never match, because the UI always sends an empty value set
> (`rule-form.component.ts:428`).

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

> ⚠️ **Cap: only "Per transaction" works, and it is not the default.** `CapScope` offers
> *Per transaction / Per period / Per payee per period*, but only **Per transaction** is
> implemented; the other two return the commission unchanged
> (`CommissionCalculator.cs:252-264`). The rule form **defaults to Per period**
> (`rule-form.component.ts:151`) — i.e. the default cap silently does nothing. Set the scope to
> **Per transaction** explicitly. A cap in a different currency to the commission is also skipped
> silently.

**Floor** works as expected (raises commission up to the floor amount).

**On screen.** The rule form shows Trigger, Measurement, Rate table (three buttons), and
collapsible Modifier / Cap / Floor sections.

---

## 5. Rate tables

Three types, all selectable.

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
calculated `hs_is_closed_won` property (`HubSpotCrmDealSource.cs:18,48,188`). **Excel import and
manual entry do not filter**: whatever you provide is accepted. The UI states this as an
expectation, not a rule.

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
| Pending → Cancelled | voided (requires a reason; only from Pending) |

A **Paid** transaction cannot be reverted to Pending — *"it has already been paid out. Use the
accounting correction workflow."*

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
| **Second KPI unlocking a rate** (e.g. margin gate on a revenue rate) | Not implemented. One measurement per rule; conditions resolve only three transaction fields. |
| **Period-scoped caps** | Declared but non-functional — only per-transaction caps apply. See 4.4. |
| **Tier accumulation across a period (Tiered tables)** | Tiered restarts per transaction. Period accumulation exists only via attainment + SplitAtQuota. |

---

## 14. Behaviours worth knowing

Collected from the verification passes — real behaviours, not bugs to work around silently.

1. **A cap left on the default scope does nothing.** Set caps to *Per transaction*.
2. **A condition on any field other than the three supported names silently never fires.**
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

---

## Maintenance

When the calculation engine, rate tables, quota/payout lifecycles, or the pay-run aggregation change,
**re-verify this document against the code and update the verification date at the top.** The
sections most likely to drift are 5, 6, 7 and 11. Prefer deleting a claim over leaving one that may
have become false.
