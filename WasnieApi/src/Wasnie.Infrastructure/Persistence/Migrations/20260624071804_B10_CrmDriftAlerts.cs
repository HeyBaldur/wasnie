using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B10_CrmDriftAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmDriftAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalDealId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TransactionStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AmountChanged = table.Column<bool>(type: "bit", nullable: false),
                    OldAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    OldCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    NewAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    NewCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    DateChanged = table.Column<bool>(type: "bit", nullable: false),
                    OldCloseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NewCloseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DetectedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmDriftAlerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmDriftAlerts_TenantId_ResolvedAt_DetectedAt",
                table: "CrmDriftAlerts",
                columns: new[] { "TenantId", "ResolvedAt", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmDriftAlerts_TenantId_Source_Deal_Transaction_Unresolved",
                table: "CrmDriftAlerts",
                columns: new[] { "TenantId", "Source", "ExternalDealId", "TransactionId" },
                unique: true,
                filter: "[ResolvedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmDriftAlerts");
        }
    }
}
