using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B37_PlanCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlanCode",
                table: "UserSubscriptions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanCode",
                table: "Tenants",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // ★★ KAN-77 — EVERY STRIPE SUBSCRIPTION MOVES TO THE €299 PLAN ("pro"), WHATEVER TIER IT WAS SOLD AS.
            //
            // The old Starter/Growth/Scale are gone; "pro" is the only plan and it is unlimited, so nobody who pays
            // loses anything by the move — and access does not depend on this column at all (it comes from the
            // subscription status, see AccountAccessPolicy), so writing it cannot interrupt anyone.
            //
            //   · UserSubscriptions WITH a Stripe id (any status): PlanCode = 'pro'. Canceled ones too — it records
            //     what the subscription is for, and a reactivation lands on the plan that exists.
            //   · Tenants whose subscription is Active or PastDue (status 0/1): PlanCode = 'pro'. A canceled or
            //     never-paying tenant keeps NULL: it has no current plan.
            //   · Rows WITHOUT a Stripe id (the old free plan) are left NULL: they never bought anything.
            //
            // ★ The legacy Tier columns are NOT touched (§B6). Nothing reads them after KAN-77.
            //
            // ⚠ THIS DOES NOT CHANGE WHAT STRIPE CHARGES. A customer on an old Growth/Scale price keeps being billed
            // that price until the subscription item is moved to the €299 price in Stripe — a decision (and an
            // action on real billing) that belongs to a person, not to a migration.
            migrationBuilder.Sql("""
                UPDATE UserSubscriptions
                SET PlanCode = 'pro'
                WHERE StripeSubscriptionId IS NOT NULL AND PlanCode IS NULL;

                UPDATE t
                SET t.PlanCode = 'pro'
                FROM Tenants t
                WHERE t.PlanCode IS NULL
                  AND EXISTS (
                      SELECT 1 FROM UserSubscriptions s
                      WHERE s.TenantId = t.Id AND s.StripeSubscriptionId IS NOT NULL AND s.Status IN (0, 1));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanCode",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "PlanCode",
                table: "Tenants");
        }
    }
}
