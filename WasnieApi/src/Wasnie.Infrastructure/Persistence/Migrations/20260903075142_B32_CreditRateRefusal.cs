using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B32_CreditRateRefusal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RateRefusal",
                table: "Credits",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Credits_TenantId_RateRefusal",
                table: "Credits",
                columns: new[] { "TenantId", "RateRefusal" },
                filter: "[RateRefusal] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Credits_TenantId_RateRefusal",
                table: "Credits");

            migrationBuilder.DropColumn(
                name: "RateRefusal",
                table: "Credits");
        }
    }
}
