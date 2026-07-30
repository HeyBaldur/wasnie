using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B19_ClawbackLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ClawbackCapPercent",
                table: "CompensationPlans",
                type: "decimal(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClawbackMaturationDays",
                table: "CompensationPlans",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayeeBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BalanceCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayeeBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayeeLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Justification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    SourceTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourcePayRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceExternalDealId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceCommissionAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DaysActive = table.Column<int>(type: "int", nullable: true),
                    MaturationDays = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayeeLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_PayeeBalances_Tenant_Payee_Currency",
                table: "PayeeBalances",
                columns: new[] { "TenantId", "PayeeId", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayeeLedgerEntries_SourceTransaction",
                table: "PayeeLedgerEntries",
                column: "SourceTransactionId",
                filter: "[SourceTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayeeLedgerEntries_Tenant_Payee_CreatedAt",
                table: "PayeeLedgerEntries",
                columns: new[] { "TenantId", "PayeeId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayeeBalances");

            migrationBuilder.DropTable(
                name: "PayeeLedgerEntries");

            migrationBuilder.DropColumn(
                name: "ClawbackCapPercent",
                table: "CompensationPlans");

            migrationBuilder.DropColumn(
                name: "ClawbackMaturationDays",
                table: "CompensationPlans");
        }
    }
}
