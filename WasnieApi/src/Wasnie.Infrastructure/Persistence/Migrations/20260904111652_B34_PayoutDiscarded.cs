using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B34_PayoutDiscarded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscardReason",
                table: "CompensationPayouts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DiscardedAt",
                table: "CompensationPayouts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscardedBy",
                table: "CompensationPayouts",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            // ★★ THE FILTERED UNIQUE INDEX HAS TO LEARN THE NEW TERMINAL STATE. IX_CompensationPayouts_Live
            // enforces "one LIVE payout per (tenant, run, payee, plan)" and excludes the terminal states
            // by name. Without adding Discarded, a discarded payout would keep occupying that slot and
            // block the recalculation that is the actual fix for the period — the queue would be clean
            // and the work still impossible.
            //
            // SQL Server does NOT support NOT IN in a filtered index predicate: it has to be <> AND <>.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS [IX_CompensationPayouts_Live] ON [CompensationPayouts]");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX [IX_CompensationPayouts_Live] " +
                "ON [CompensationPayouts] ([TenantId], [PayRunId], [PayeeId], [PlanId]) " +
                "WHERE [Status] <> 'Paid' AND [Status] <> 'Disputed' AND [Status] <> 'Discarded' " +
                "AND [PayRunId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the A6 predicate before the column disappears.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS [IX_CompensationPayouts_Live] ON [CompensationPayouts]");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX [IX_CompensationPayouts_Live] " +
                "ON [CompensationPayouts] ([TenantId], [PayRunId], [PayeeId], [PlanId]) " +
                "WHERE [Status] <> 'Paid' AND [Status] <> 'Disputed' AND [PayRunId] IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "DiscardReason",
                table: "CompensationPayouts");

            migrationBuilder.DropColumn(
                name: "DiscardedAt",
                table: "CompensationPayouts");

            migrationBuilder.DropColumn(
                name: "DiscardedBy",
                table: "CompensationPayouts");
        }
    }
}
