using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B21_ClawbackChurnMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EventDate",
                table: "PayeeLedgerEntries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourcePlanId",
                table: "PayeeLedgerEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayeeLedgerEntries_ChurnPerTransactionPlan",
                table: "PayeeLedgerEntries",
                columns: new[] { "SourceTransactionId", "SourcePlanId" },
                unique: true,
                filter: "[SourceType] = 'DealChurn' AND [SourceTransactionId] IS NOT NULL AND [SourcePlanId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_PayeeLedgerEntries_ChurnPerTransactionPlan",
                table: "PayeeLedgerEntries");

            migrationBuilder.DropColumn(
                name: "EventDate",
                table: "PayeeLedgerEntries");

            migrationBuilder.DropColumn(
                name: "SourcePlanId",
                table: "PayeeLedgerEntries");
        }
    }
}
