# 15 — Aggregation Queries Audit Checklist

**Reading time:** ~3 min  
**Applies to:** ALL Wasnie aggregation endpoints (SUM, COUNT, GROUP BY over Credits / Transactions / related collections)

---

## Purpose

Aggregation over financial data is the highest-risk area for silent data-corruption bugs. The same bug has appeared twice in different handlers (A.3-FIX-2, V3-FIX-2). This checklist ensures every aggregation is audited before it ships AND whenever a related bug is reported.

---

## The canonical Wasnie aggregation rule

When computing what a payee **earned** (their compensation/commission):

- ✅ Use **`CreditedAmount`** — what the payee was actually compensated.
- ❌ Never use **`OriginalAmount`** — that is the raw transaction revenue (sale price).

`CreditedAmount = OriginalAmount × commission_rate`. Using OriginalAmount inflates the result by `1 / commission_rate` (e.g., ×20 for a 5% flat plan).

`OriginalAmount` is legitimate only when explicitly displaying the **source transaction amount** alongside the credit (e.g., the credit detail page, export columns that show both).

---

## Audit procedure

For each aggregation endpoint:

1. Extract the LINQ query generating the aggregation.
2. Call `.ToQueryString()` and inspect the generated SQL for unexpected JOINs or missing WHERE clauses.
3. Run the SQL against the dev DB for a known payee (Agnieszka EMP301) for June 2026. Expected CreditedAmount total = **€342.76** (3 credits).
4. Compare to ground truth. If inflated by a constant factor (e.g., 20×), it is the OriginalAmount bug. If inflated by a growing factor, it is a Cartesian product.
5. Mark Status ✓ Correct or ❌ Broken and file a WI.

---

## Audit table (last updated 2026-06-08)

| Endpoint | Handler | Aggregation | Field used | Status | Last audited | Notes |
|---|---|---|---|---|---|---|
| GET /payees/:id/dashboard — attainment gauges | `GetPayeeDashboardHandler` | In-memory SUM | `allCredits` loaded via `c.CreditedAmount.*` | ✓ Correct | 2026-06-08 | |
| GET /payees/:id/dashboard — earnings trend | `GetPayeeDashboardHandler` | DB query → in-memory GroupBy | `c.CreditedAmount.Amount` | ✓ Correct | 2026-06-08 | **Fixed in V3-FIX-2**; was using OriginalAmount (20× inflation) |
| GET /payees/:id/attainment | `GetPayeeAttainmentHandler` | DB query SUM | `c.CreditedAmount.Amount` + currency filter | ✓ Correct | 2026-06-08 | |
| QuotaAttainmentService — Revenue | `QuotaAttainmentService` | DB query SUM | `c.CreditedAmount.Amount` + currency filter | ✓ Correct | 2026-06-08 | **Fixed in A.3-FIX-2**; was using OriginalAmount |
| QuotaAttainmentService — Units | `QuotaAttainmentService` | DB query SUM | `t.Quantity` | ✓ Correct | 2026-06-08 | |
| GET /credits (counters) | `GetCreditCountersHandler` | DB query → in-memory GroupBy | `c.CreditedAmount.Amount` | ✓ Correct | 2026-06-08 | |
| GET /credits/by-payee | `GetCreditsByPayeeHandler` | DB query → in-memory GroupBy | `c.CreditedAmount.Amount` | ✓ Correct | 2026-06-08 | |
| GET /credits (list sort) | `ListCreditsHandler` | ORDER BY only | `c.OriginalAmount.Amount` | ✓ Intentional | 2026-06-08 | Sort by revenue (user-selectable); no aggregation |
| GET /credits/:id (detail) | `GetCreditByIdHandler` | Display only | Both OriginalAmount + CreditedAmount | ✓ Intentional | 2026-06-08 | Shows both for transparency |
| GET /credits/export | `ExportCreditsHandler` | Display only | Both OriginalAmount + CreditedAmount | ✓ Intentional | 2026-06-08 | Export includes both columns |

---

## When to re-run this audit

1. Any time a new aggregation endpoint is added.
2. Any time a similar inflation bug is reported (OriginalAmount or Cartesian product).
3. Before any major Payout Engine (A.4) or reporting endpoint ship.
4. After any schema change to Credit or CompensationTransaction entities.

---

## How to extract SQL for inspection

```csharp
// In any handler that has access to IApplicationDbContext:
var sql = db.Credits
    .Where(c => c.PayeeId == payeeId && c.SupersededAt == null)
    .Select(c => new { c.CreditedAmount.Amount })
    .ToQueryString();
Console.WriteLine(sql);
```

Or add a temporary `_logger.LogDebug(query.ToQueryString())` before `ToListAsync()`. Remove before commit.

---

## Future audit scope (not yet done)

- Payout Engine aggregations (A.4) — must be audited when designed.
- Any new "totals" or "counters" API surface.
- Tenant-level summary reports (if added in Phase 3+).
