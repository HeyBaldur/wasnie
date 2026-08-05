/* ============================================================================================
   BACKFILL — CompensationPayouts.PaidAt
   ============================================================================================

   WHY
   ---
   PaidAt was added so the dashboard's "Payouts" card can report CASH FLOW: money that actually
   left, attributed to the day it left. Every payout written before that column existed has
   PaidAt = NULL, so without this backfill every historical month collapses to zero.

   WHAT IT DOES
   ------------
   For rows with Status = 'Paid' and PaidAt IS NULL, sets PaidAt = UpdatedAt.
   It touches nothing else. Rows in any other status keep PaidAt = NULL, which is the invariant
   the domain enforces (PaidAt has a value if and only if Status = Paid).

   WHY UpdatedAt IS THE RIGHT SOURCE (verified, not assumed)
   ---------------------------------------------------------
   UpdatedAt is written by exactly six domain methods on CompensationPayout: Calculate, Approve,
   MarkPaid, RevertToCalculated, RevertPaidToApproved and Dispute. Of those, every one except
   MarkPaid either leaves the payout in a status other than Paid or throws when it is Paid
   (Dispute refuses Paid rows outright). AssignToRun does not touch UpdatedAt, and no raw SQL or
   ExecuteUpdate anywhere in the codebase writes to this table.

   Therefore, for a row whose CURRENT status is Paid, the last write to UpdatedAt was necessarily
   the MarkPaid call — UpdatedAt IS the payment timestamp, not an approximation of it. Where a
   payout was paid, reverted and paid again, UpdatedAt holds the LATEST payment, which is the
   correct cash date anyway.

   Cross-checked against the audit trail on the development database: for every Paid payout with a
   PAYOUT_CREDITS_CONSUMED entry (written inside the same MarkPaid transaction), the audit
   timestamp and UpdatedAt agree to the second — DATEDIFF(second, ...) = 0 on every row.

   This is why the fallback contemplated in the WI (using Period.End for historical rows) is NOT
   used: it would be strictly worse, and it is what produced the wrong numbers in the first place.

   SAFETY
   ------
   · Idempotent   — the WHERE clause excludes rows that already have PaidAt, so re-running is a
                    no-op. Safe to run twice, or to run after a partial failure.
   · Scoped       — only Status = 'Paid' AND PaidAt IS NULL.
   · Reversible   — see the rollback block at the bottom.
   · Transactional— wrapped in an explicit transaction; nothing is half-applied.
   · Non-destructive — writes a column that is NULL in every affected row. No existing value is
                    overwritten, so no information is lost even if the decision is later revised.

   HOW TO RUN
   ----------
   1. Run the PREVIEW block alone first and read the numbers.
   2. Run the APPLY block.
   3. Run the VERIFY block; it must report 0 remaining and 0 invariant violations.

   Requires migration AddPayoutPaidAt to have been applied (the column must exist).
   ============================================================================================ */

SET NOCOUNT ON;

-- REQUIRED. CompensationPayouts carries a filtered index (IX_CompensationPayouts_Live), and SQL Server
-- refuses any UPDATE on such a table unless QUOTED_IDENTIFIER is ON. SSMS sets it ON by default;
-- sqlcmd -Q does NOT, so without this line the APPLY block fails with Msg 1934.
SET QUOTED_IDENTIFIER ON;

/* ── 1. PREVIEW — run this on its own and read it before applying anything ──────────────────── */

-- How many rows would change, and what the resulting cash-flow months would look like.
SELECT
    TenantId,
    PaymentMonth = FORMAT(UpdatedAt, 'yyyy-MM'),
    Currency     = TotalCommissionCurrency,
    Payouts      = COUNT(*),
    Total        = SUM(TotalCommissionAmount)
FROM CompensationPayouts
WHERE Status = 'Paid'
  AND PaidAt IS NULL
GROUP BY TenantId, FORMAT(UpdatedAt, 'yyyy-MM'), TotalCommissionCurrency
ORDER BY TenantId, PaymentMonth, Currency;

-- Sanity guard: rows where UpdatedAt precedes CalculatedAt would mean the timestamps are not
-- trustworthy on this database. Expected result: 0 rows. Investigate before applying if not.
SELECT SuspectRows = COUNT(*)
FROM CompensationPayouts
WHERE Status = 'Paid'
  AND PaidAt IS NULL
  AND UpdatedAt < CalculatedAt;


/* ── 2. APPLY ───────────────────────────────────────────────────────────────────────────────── */

BEGIN TRANSACTION;

    UPDATE CompensationPayouts
       SET PaidAt = UpdatedAt
     WHERE Status = 'Paid'
       AND PaidAt IS NULL;

    PRINT CONCAT('Rows backfilled: ', @@ROWCOUNT);

COMMIT TRANSACTION;


/* ── 3. VERIFY — both counts must be 0 ──────────────────────────────────────────────────────── */

SELECT
    -- Paid rows still missing a payment date.
    StillMissing = (SELECT COUNT(*) FROM CompensationPayouts
                     WHERE Status = 'Paid' AND PaidAt IS NULL),
    -- The domain invariant: PaidAt has a value if and only if Status = Paid.
    InvariantViolations = (SELECT COUNT(*) FROM CompensationPayouts
                            WHERE (Status = 'Paid' AND PaidAt IS NULL)
                               OR (Status <> 'Paid' AND PaidAt IS NOT NULL));


/* ── 4. ROLLBACK — only if the decision is reversed ─────────────────────────────────────────── */
/*
   Clears exactly what this script set: Paid rows whose PaidAt still equals UpdatedAt, i.e. rows
   untouched by any real payment since. A payout paid through the application AFTER the backfill
   has PaidAt set at MarkPaid time and UpdatedAt equal to it as well — so if any real payments
   have happened since, restrict this further by date before running it.

BEGIN TRANSACTION;

    UPDATE CompensationPayouts
       SET PaidAt = NULL
     WHERE Status = 'Paid'
       AND PaidAt = UpdatedAt;

    PRINT CONCAT('Rows reverted: ', @@ROWCOUNT);

COMMIT TRANSACTION;
*/
