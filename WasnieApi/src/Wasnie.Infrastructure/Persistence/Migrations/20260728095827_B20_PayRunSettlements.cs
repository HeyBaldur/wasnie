using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B20_PayRunSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayRunSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    GrossCommission = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    GrossCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ClawbackWithheld = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithheldCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    NetPaid = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    NetCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CarryoverRemaining = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CarryoverCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AppliedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayRunSettlements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayRunSettlements_Tenant_Payee_AppliedAt",
                table: "PayRunSettlements",
                columns: new[] { "TenantId", "PayeeId", "AppliedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_PayRunSettlements_Run_Payee_Currency",
                table: "PayRunSettlements",
                columns: new[] { "TenantId", "PayRunId", "PayeeId", "Currency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayRunSettlements");
        }
    }
}
