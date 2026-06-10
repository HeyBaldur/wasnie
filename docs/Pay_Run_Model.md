# Wasnie — Pay Run Model (Design Document)

**Status:** APPROVED — decisions locked, ready for A.6 implementation prompt
**Created:** 2026-06-08
**Author:** Rodolfo A. Calvo Jaubert (with Claude)
**Scope:** Design of the Pay Run concept for the payouts subsystem (target WI: A.6)
**Precedence note:** Once A.6 starts, reconcile with `Wasnie_Product_Master_Specification.md` and any affected `architecture/` section files BEFORE writing code. Does not override ARCHITECTURE.md.

---

## 1. Why this document exists

A.4 (Payout Engine) and A.5 (Payouts UI) shipped a working payout calculation: transactions → credits → payout lines → a `CompensationPayout` per (payee, plan, period), with a `Calculated → Approved → Paid | Disputed` state machine.

In real use, one structural concept is missing: the **Pay Run** — the unit that represents "the act of closing and paying one period for everyone at once." Without it, the payouts list is a flat, unbounded table mixing every payee and every period together. With 50–300 payees (Wasnie's target market) × many months, this is unmanageable.

This is not a cosmetic problem. It is the absence of the central organising structure of the commission-processing domain. Every comparable platform (Xactly, CaptivateIQ, Spiff, beqom) is built around a period-batch concept.

**Important:** the current calculation is NOT broken. `$0` payouts are correct — they are assigned payees who made no sales in the period. The problem is presentation and workflow structure, not math.

---

## 2. The domain in plain terms

Commission processing mirrors how payroll and accounting close a period:

1. **Open period.** During (say) June, sales come in and generate credits. Nothing is paid yet; everything can still change.
2. **Calculate the run.** At close, the admin calculates the WHOLE period at once. The system produces one payout per assigned payee — some with amounts, some `$0`. This is a draft.
3. **Review.** The admin looks at the run total ("June: 47 payees, €120,000") and drills into the individuals they care about. Because the period is still Draft, anything can be recalculated.
4. **Approve.** Someone with authority signs off. This is the quality gate BEFORE money leaves.
5. **Pay / close.** Marked paid and the period is **locked**. From here, this period is immutable history.
6. **Error after close?** The period is NOT reopened. A correction (adjustment / clawback) is made in the NEXT open period. The historical payment is never altered.

The Pay Run is the object that carries this lifecycle. Individual payouts live *inside* a run.

---

## 3. Proposed model

### 3.1 New aggregate: `PayRun`

A `PayRun` represents one calculation batch for one period within one tenant.

Fields:
- `Id`
- `TenantId`
- `PeriodStart` / `PeriodEnd`
- `Status` — see §3.3
- Lifecycle audit: `CreatedAt` / `CreatedBy`, `ApprovedAt` / `ApprovedBy`, `PaidAt` / `PaidBy`
- Denormalised roll-ups (recomputed on each calculation, payout lines remain source of truth):
  - `PayeeCount` — **all** payees in the run, including those with `$0` commission
  - `PaidPayeeCount` — payees with `TotalCommission > 0` only
  - `TotalAmounts` — per-currency breakdown (e.g. `{ "EUR": 95000.00, "PLN": 87500.00 }`); not affected by `$0` payouts
  - `ZeroPayoutCount` — count of payees with `$0` commission (= `PayeeCount − PaidPayeeCount`)

**Roll-up rationale:** the two counters (`PayeeCount` / `PaidPayeeCount`) are exposed separately to avoid the UX confusion "the run says 47 payees but I only see 31" — the discrepancy is always explained by the zero-payout toggle.

### 3.2 Relationship to existing entities

- A `PayRun` **has many** `CompensationPayout`.
- `CompensationPayout` gains a `PayRunId` foreign key.
- `PayoutLine` is unchanged (still 1:1 with `Credit`).
- **Step 0 of A.6 must confirm** whether the existing filtered unique index `(TenantId, PayeeId, PlanId, PeriodStart, PeriodEnd)` can be simplified to `(PayRunId, PayeeId, PlanId)` now that the run owns the period. Do not assume this — verify against the current schema first.

**The A.4 foundation is reused, not discarded.** Payouts and lines stay. The run is a parent grouping + lifecycle owner added on top.

### 3.3 State machines — two levels

**Run level:** `Draft → Approved → Paid` (Paid = locked, no separate Lock state)

| Transition | Who | Effect |
|---|---|---|
| `(new) → Draft` | Calculate action | Run created; payouts generated for all assigned payees in period |
| `Draft → Draft` | Recalculate | Idempotent re-run; existing Draft payouts replaced |
| `Draft → Approved` | Approve Run | Run and all its payouts approved in one action |
| `Approved → Draft` | Reopen | Run and **all** its payouts revert to Draft — see reopen rule below |
| `Approved → Paid` | Mark Paid | Run closed; period locked; no further edits |

**Reopen rule (Decision 3):** reopen is always run-wide. When an Approved run is reopened, the entire run reverts to Draft — all payees, all currencies at once. Partial reopen (reopening a single payee or a single currency sub-group) is not allowed. This preserves the invariant that a run never has a mix of Approved and Draft payouts, which would create ambiguous roll-up states.

**Payout level (existing A.4 machine):** `Calculated → Approved → Paid | Disputed`

Individual payouts inherit run transitions when the admin acts at run level:
- Run `Draft → Approved` → all its `Calculated` payouts → `Approved`
- Run `Approved → Draft` (Reopen) → all its `Approved` payouts → `Calculated`
- Run `Approved → Paid` → all its `Approved` payouts → `Paid`

A single payout can still transition individually (e.g. one payee `Disputed` while the rest of the run proceeds). A `Disputed` payout does not block the run from being Approved or Paid — the run-level action applies to all non-Disputed payouts.

### 3.4 Multi-currency runs (Decision 2)

One `PayRun` per period covers all currencies. A June run for a tenant with EUR and PLN payees produces a single run containing both EUR and PLN payouts.

- Roll-ups use a per-currency breakdown (see §3.1 `TotalAmounts`).
- The run list shows: "June 2026 — €95,000 + zł87,500 — 47 payees".
- **Reopen and recalculate always operate on the full run**, including all currencies. It is not possible to reopen or recalculate only the EUR sub-group of a run while leaving the PLN sub-group Approved. This constraint is the direct consequence of Decision 3 (no partial reopen) applied to the multi-currency case.

### 3.5 Zero-payout handling (Decision 5)

Generate `$0` payouts for all assigned payees in the period. Do not suppress them.

- They appear in the run-detail view, hidden by default behind a **"Show $0 payouts"** toggle (default off).
- They are included in `PayeeCount` but not in `PaidPayeeCount`.
- They are not included in `TotalAmounts` (adding zero changes nothing, but the intent is clear).
- They carry the full audit trail: a `$0` payout proves "we looked at this payee for this period and there was nothing to pay." This is consistent with ARCHITECTURE.md audit-trail philosophy.

---

## 4. Decisions — all locked

| # | Question | Decision |
|---|---|---|
| 1 | Approve/Pay granularity | Run-level with individual drill-down. Approve "June" in one action; handle one payee individually when needed. |
| 2 | Pattern B run shape | **(a)** One `PayRun` per period, multi-currency. Per-currency roll-ups in `TotalAmounts`. |
| 3 | Reopen scope | Run-wide only. Reopen reverts all payees and all currencies to Draft simultaneously. No partial reopen. |
| 4 | Lock as explicit state | Folded into Paid. `Paid = locked`. No separate Lock transition. |
| 5 | Zero-payout handling | Generate + hide by default. Toggle "Show $0 payouts". Two counters: `PayeeCount` (all) and `PaidPayeeCount` (>$0 only). |
| 6 | Adjustments / clawbacks | **Separate WI after A.6.** Not in scope. |

---

## 5. Navigation (UI consequence)

Two levels replace the current flat list:

- **Run list** (`/payouts` or `/pay-runs`): one row per run — period, payee counts (`PaidPayeeCount / PayeeCount`), total amounts per currency, status. Default-filtered to recent periods.
- **Run detail** (`/pay-runs/:id`): payees in that run — filterable, sortable, with the "Show $0 payouts" toggle. Run-level actions (Approve / Mark Paid / Reopen) live here.
- **Payout statement** (`/payouts/:id`, existing A.5 page): unchanged, reached by drilling into a payee within a run.

The A.5 work is reused: statement detail, PDF export, and per-payout machine all remain. What changes is the entry point (run list → run detail → statement) and the addition of run-level actions.

---

## 6. Interim mitigation (independent of A.6)

The full Pay Run model is a large WI. Meanwhile the current A.5 list is hard to work with. A small, UI-only change can reduce the noise WITHOUT building the run model:

- Default the existing list filter to the **current period** instead of showing all history.
- Add a **"hide $0 payouts"** toggle (default on).

These survive the A.6 redesign — they get reused inside the run-detail view. Not throwaway work.

---

## 7. What A.6 covers

- **Domain:** new `PayRun` aggregate + `PayRunId` FK on `CompensationPayout` + run state machine + Reopen transition.
- **Engine:** calculation command groups payouts into a run + computes run roll-ups. Recalculation is idempotent within a Draft run.
- **UI:** run list + run detail + run-level actions (Approve / Mark Paid / Reopen) + "Show $0 payouts" toggle.
- **Tests:** embedded as every WI since A.3.
- **Out of scope:** adjustments/clawbacks (future WI), FX conversion (future WI).

**A.6 must start with a read-only Step 0** confirming the current payout schema, the existing filtered index, and the calculation command structure before any code is written.

---

## 8. Next step

This document is approved. Before starting A.6 implementation:
1. Reconcile with `Wasnie_Product_Master_Specification.md` — add PayRun to the domain model section.
2. Reconcile with the relevant `architecture/` section files.
3. Run Step 0 to confirm schema, index, and calculation command details.
4. Convert this document into the A.6 implementation prompt(s).
