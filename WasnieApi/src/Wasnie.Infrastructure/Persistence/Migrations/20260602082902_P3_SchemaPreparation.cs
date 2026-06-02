using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P3_SchemaPreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectivePeriodEnd",
                table: "PlanRules",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectivePeriodStart",
                table: "PlanRules",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tag",
                table: "PlanRules",
                type: "nvarchar(50)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SupersededAt",
                table: "Credits",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupersededBy",
                table: "Credits",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeriodType",
                table: "CompensationPlans",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Credits_TenantId_SupersededAt",
                table: "Credits",
                columns: new[] { "TenantId", "SupersededAt" },
                filter: "[SupersededAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Credits_TenantId_SupersededAt",
                table: "Credits");

            migrationBuilder.DropColumn(
                name: "EffectivePeriodEnd",
                table: "PlanRules");

            migrationBuilder.DropColumn(
                name: "EffectivePeriodStart",
                table: "PlanRules");

            migrationBuilder.DropColumn(
                name: "Tag",
                table: "PlanRules");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "Credits");

            migrationBuilder.DropColumn(
                name: "SupersededBy",
                table: "Credits");

            migrationBuilder.DropColumn(
                name: "PeriodType",
                table: "CompensationPlans");
        }
    }
}
