using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class A8_AddTenantHasSelectedPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasSelectedPlan",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasSelectedPlan",
                table: "Tenants");
        }
    }
}
