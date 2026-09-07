using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B33_ReconciliationClosures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReconciliationClosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryKind = table.Column<int>(type: "int", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FactOccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ClosedByEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationClosures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationClosures_Tenant_Entry_Reason_Fact",
                table: "ReconciliationClosures",
                columns: new[] { "TenantId", "EntryKind", "EntityId", "Reason", "FactOccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconciliationClosures");
        }
    }
}
