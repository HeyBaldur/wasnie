using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B14_CreditUniqueLiveIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_Credits_Tenant_Transaction_Plan_Rule_Live",
                table: "Credits",
                columns: new[] { "TenantId", "TransactionId", "PlanId", "RuleId" },
                unique: true,
                filter: "[SupersededAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Credits_Tenant_Transaction_Plan_Rule_Live",
                table: "Credits");
        }
    }
}
