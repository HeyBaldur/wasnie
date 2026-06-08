using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class A4_AddPayoutPlanId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                table: "CompensationPayouts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Unique filtered index on (TenantId, PayeeId, PlanId, PeriodStart, PeriodEnd)
            // where Status NOT IN ('Paid', 'Disputed').
            // PeriodStart/PeriodEnd are owned-type columns — cannot be expressed via HasIndex
            // lambda so the index is created here with raw SQL.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX [IX_CompensationPayouts_Live] " +
                "ON [CompensationPayouts] ([TenantId], [PayeeId], [PlanId], [PeriodStart], [PeriodEnd]) " +
                "WHERE [Status] <> 'Paid' AND [Status] <> 'Disputed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS [IX_CompensationPayouts_Live] ON [CompensationPayouts]");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "CompensationPayouts");
        }
    }
}
