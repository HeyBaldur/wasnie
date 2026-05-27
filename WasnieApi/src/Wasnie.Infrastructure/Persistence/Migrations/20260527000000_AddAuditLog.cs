using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wasnie.Infrastructure.Persistence;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260527000000_AddAuditLog")]
    /// <inheritdoc />
    public partial class AddAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ActorEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResourceId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ResourceDisplayName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Metadata = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_TimestampUtc",
                table: "AuditLogs",
                columns: new[] { "TenantId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_ResourceType_ResourceId_TimestampUtc",
                table: "AuditLogs",
                columns: new[] { "TenantId", "ResourceType", "ResourceId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_ActorUserId_TimestampUtc",
                table: "AuditLogs",
                columns: new[] { "TenantId", "ActorUserId", "TimestampUtc" });

            // Immutability trigger: prevent UPDATE and DELETE on AuditLogs (Rule 5.3.1, 5.3.2)
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_AuditLogs_Immutable
ON AuditLogs
AFTER UPDATE, DELETE
AS
BEGIN
    RAISERROR('AuditLog records are immutable and cannot be modified or deleted.', 16, 1);
    ROLLBACK TRANSACTION;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_AuditLogs_Immutable");

            migrationBuilder.DropTable(name: "AuditLogs");
        }
    }
}
