using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B8_CrmOwnerMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmOwnerMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CrmOwnerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmOwnerMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmOwnerMappings_TenantId_PayeeId",
                table: "CrmOwnerMappings",
                columns: new[] { "TenantId", "PayeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmOwnerMappings_TenantId_Source_CrmOwnerId",
                table: "CrmOwnerMappings",
                columns: new[] { "TenantId", "Source", "CrmOwnerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmOwnerMappings");
        }
    }
}
