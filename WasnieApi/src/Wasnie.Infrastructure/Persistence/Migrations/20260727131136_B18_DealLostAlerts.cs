using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B18_DealLostAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DealLostAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalDealId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TransactionStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CommissionCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DetectedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DealLostAlerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DealLostAlerts_TenantId_ResolvedAt_DetectedAt",
                table: "DealLostAlerts",
                columns: new[] { "TenantId", "ResolvedAt", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DealLostAlerts_TenantId_Source_Transaction_Unresolved",
                table: "DealLostAlerts",
                columns: new[] { "TenantId", "Source", "TransactionId" },
                unique: true,
                filter: "[ResolvedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DealLostAlerts");
        }
    }
}
