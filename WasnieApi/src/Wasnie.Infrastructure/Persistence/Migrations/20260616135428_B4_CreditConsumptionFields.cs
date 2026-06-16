using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B4_CreditConsumptionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConsumedAt",
                table: "Credits",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConsumedByPayoutId",
                table: "Credits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Credits_ConsumedByPayoutId",
                table: "Credits",
                column: "ConsumedByPayoutId",
                filter: "[ConsumedByPayoutId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Credits_TenantId_ConsumedAt",
                table: "Credits",
                columns: new[] { "TenantId", "ConsumedAt" },
                filter: "[ConsumedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Credits_ConsumedByPayoutId",
                table: "Credits");

            migrationBuilder.DropIndex(
                name: "IX_Credits_TenantId_ConsumedAt",
                table: "Credits");

            migrationBuilder.DropColumn(
                name: "ConsumedAt",
                table: "Credits");

            migrationBuilder.DropColumn(
                name: "ConsumedByPayoutId",
                table: "Credits");
        }
    }
}
