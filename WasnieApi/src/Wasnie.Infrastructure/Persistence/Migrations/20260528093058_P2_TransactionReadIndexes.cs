using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2_TransactionReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_IngestedAt",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "IngestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_Status",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_TransactionDate",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "TransactionDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompensationTransactions_TenantId_IngestedAt",
                table: "CompensationTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CompensationTransactions_TenantId_Status",
                table: "CompensationTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CompensationTransactions_TenantId_TransactionDate",
                table: "CompensationTransactions");
        }
    }
}
