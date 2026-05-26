using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaginationIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Quotas_TenantId_PayeeId",
                table: "Quotas",
                columns: new[] { "TenantId", "PayeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotas_TenantId_Status",
                table: "Quotas",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanAssignments_TenantId_PayeeId",
                table: "PlanAssignments",
                columns: new[] { "TenantId", "PayeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanAssignments_TenantId_Status",
                table: "PlanAssignments",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Payees_TenantId_FullName",
                table: "Payees",
                columns: new[] { "TenantId", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_Payees_TenantId_Status",
                table: "Payees",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationPlans_TenantId_Status",
                table: "CompensationPlans",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotas_TenantId_PayeeId",
                table: "Quotas");

            migrationBuilder.DropIndex(
                name: "IX_Quotas_TenantId_Status",
                table: "Quotas");

            migrationBuilder.DropIndex(
                name: "IX_PlanAssignments_TenantId_PayeeId",
                table: "PlanAssignments");

            migrationBuilder.DropIndex(
                name: "IX_PlanAssignments_TenantId_Status",
                table: "PlanAssignments");

            migrationBuilder.DropIndex(
                name: "IX_Payees_TenantId_FullName",
                table: "Payees");

            migrationBuilder.DropIndex(
                name: "IX_Payees_TenantId_Status",
                table: "Payees");

            migrationBuilder.DropIndex(
                name: "IX_CompensationPlans_TenantId_Status",
                table: "CompensationPlans");
        }
    }
}
