using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B42_AssistantTokenBoosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssistantBoostDebits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IncludedLimit = table.Column<long>(type: "bigint", nullable: false),
                    UsedInPeriod = table.Column<long>(type: "bigint", nullable: false),
                    Tokens = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantBoostDebits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssistantTokenBoosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StripeEventId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StripeProductId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StripeSessionId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Tokens = table.Column<long>(type: "bigint", nullable: false),
                    PurchasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantTokenBoosts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AssistantBoostDebits_TenantId_PeriodStart",
                table: "AssistantBoostDebits",
                columns: new[] { "TenantId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTokenBoosts_TenantId_PurchasedAt",
                table: "AssistantTokenBoosts",
                columns: new[] { "TenantId", "PurchasedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_AssistantTokenBoosts_StripeEventId",
                table: "AssistantTokenBoosts",
                column: "StripeEventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistantBoostDebits");

            migrationBuilder.DropTable(
                name: "AssistantTokenBoosts");
        }
    }
}
