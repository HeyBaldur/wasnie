using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B6_PayRunSupplementalSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayRuns_Unique",
                table: "PayRuns");

            migrationBuilder.AddColumn<int>(
                name: "SupplementalSequence",
                table: "PayRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PayRuns_Unique",
                table: "PayRuns",
                columns: new[] { "TenantId", "PeriodStart", "PeriodEnd", "SupplementalSequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayRuns_Unique",
                table: "PayRuns");

            migrationBuilder.DropColumn(
                name: "SupplementalSequence",
                table: "PayRuns");

            migrationBuilder.CreateIndex(
                name: "IX_PayRuns_Unique",
                table: "PayRuns",
                columns: new[] { "TenantId", "PeriodStart", "PeriodEnd" },
                unique: true);
        }
    }
}
