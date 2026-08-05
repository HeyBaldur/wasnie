using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B23_AddPayoutPaidAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAt",
                table: "CompensationPayouts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompensationPayouts_Tenant_Status_PaidAt",
                table: "CompensationPayouts",
                columns: new[] { "TenantId", "Status", "PaidAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompensationPayouts_Tenant_Status_PaidAt",
                table: "CompensationPayouts");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "CompensationPayouts");
        }
    }
}
