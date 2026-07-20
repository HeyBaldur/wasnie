using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B9_FilteredUniqueIndexesExcludeCancelled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompensationTransactions_TenantId_ReferenceNumber",
                table: "CompensationTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CompensationTransactions_TenantId_Source_ExternalId",
                table: "CompensationTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_ReferenceNumber",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "ReferenceNumber" },
                unique: true,
                filter: "[Status] <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_Source_ExternalId",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "Source", "ExternalId" },
                unique: true,
                filter: "[ExternalId] IS NOT NULL AND [Status] <> 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompensationTransactions_TenantId_ReferenceNumber",
                table: "CompensationTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CompensationTransactions_TenantId_Source_ExternalId",
                table: "CompensationTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_ReferenceNumber",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "ReferenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_Source_ExternalId",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "Source", "ExternalId" },
                unique: true,
                filter: "[ExternalId] IS NOT NULL");
        }
    }
}
