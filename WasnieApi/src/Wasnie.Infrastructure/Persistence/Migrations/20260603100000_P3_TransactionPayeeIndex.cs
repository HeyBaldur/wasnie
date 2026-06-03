using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P3_TransactionPayeeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Support efficient multi-payee filtering (WI-PROD-I.2).
            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_PayeeId",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "PayeeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompensationTransactions_TenantId_PayeeId",
                table: "CompensationTransactions");
        }
    }
}
