using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2_TransactionDomainSurgery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ExternalReference",
                table: "CompensationTransactions",
                newName: "ExternalId");

            migrationBuilder.AlterColumn<int>(
                name: "Tier",
                table: "Tenants",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 2);

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_Source_ExternalId",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "Source", "ExternalId" },
                unique: true,
                filter: "[ExternalId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompensationTransactions_TenantId_Source_ExternalId",
                table: "CompensationTransactions");

            migrationBuilder.RenameColumn(
                name: "ExternalId",
                table: "CompensationTransactions",
                newName: "ExternalReference");

            migrationBuilder.AlterColumn<int>(
                name: "Tier",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
