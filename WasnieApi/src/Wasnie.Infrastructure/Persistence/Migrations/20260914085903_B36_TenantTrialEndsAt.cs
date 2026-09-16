using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B36_TenantTrialEndsAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrialEndsAt",
                table: "Tenants",
                type: "datetimeoffset",
                nullable: true);

            // ★★ KAN-77 — EXISTING TENANTS MOVE ACCORDING TO THEIR REAL STATE, NOT A TRIAL FOR EVERYONE.
            //
            // The account state is derived (AccountAccessPolicy), so the only fact to write is who gets a trial:
            //
            //   · Has a Stripe subscription, Active/PastDue  → PAYING. No trial written; access comes from the
            //     subscription and is NEVER interrupted by this migration.
            //   · Has a Stripe subscription, Canceled/Incomplete → no trial written → paywall. They had their
            //     chance to subscribe; the new trial is not a free month for people who already left.
            //   · No Stripe subscription at all (the old Free plan — including its "Active" rows without a
            //     Stripe id — and tenants that only carry a paid tier from the beta default or seeds) → a trial
            //     of 7 days counted from this migration, so nobody is locked out on deploy day without warning.
            //
            // ★ "Paying" is decided by StripeSubscriptionId, NOT by Status = Active: the old select-free endpoint
            // wrote Active rows with no Stripe id, and treating those as paying would exempt them forever.
            //
            // ★ 7 IS WRITTEN HERE, NOT READ FROM Billing:TrialDays. A migration cannot read app configuration,
            // and this is a one-time fact about a past event (the day trials were introduced). New registrations
            // do read the configured value.
            //
            // Idempotent on its own column: only rows still without a trial are touched.
            migrationBuilder.Sql("""
                UPDATE t
                SET t.TrialEndsAt = DATEADD(day, 7, SYSDATETIMEOFFSET())
                FROM Tenants t
                WHERE t.TrialEndsAt IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM UserSubscriptions s
                      WHERE s.TenantId = t.Id AND s.StripeSubscriptionId IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrialEndsAt",
                table: "Tenants");
        }
    }
}
