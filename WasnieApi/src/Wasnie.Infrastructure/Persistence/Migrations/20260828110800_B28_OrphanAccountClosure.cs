using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B28_OrphanAccountClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AccountClosedAt",
                table: "Payees",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountClosedBy",
                table: "Payees",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "Credits",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedBy",
                table: "Credits",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureNote",
                table: "Credits",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureReason",
                table: "Credits",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Credits_TenantId_PayeeId_Outstanding",
                table: "Credits",
                columns: new[] { "TenantId", "PayeeId" },
                filter: "[SupersededAt] IS NULL AND [ConsumedAt] IS NULL AND [ClosedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Credits_TenantId_PayeeId_Outstanding",
                table: "Credits");

            migrationBuilder.DropColumn(
                name: "AccountClosedAt",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "AccountClosedBy",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Credits");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "Credits");

            migrationBuilder.DropColumn(
                name: "ClosureNote",
                table: "Credits");

            migrationBuilder.DropColumn(
                name: "ClosureReason",
                table: "Credits");
        }
    }
}
