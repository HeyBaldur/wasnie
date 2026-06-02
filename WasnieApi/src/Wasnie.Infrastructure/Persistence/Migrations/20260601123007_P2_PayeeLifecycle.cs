using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2_PayeeLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedAt",
                table: "Payees",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Payees",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PayeeId",
                table: "CompensationTransactions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            // Decision D + Decision 12: seed Transaction.PayeeId as Optional for all existing tenants.
            // Existing transaction rows keep their non-null PayeeId — only the schema allows null going forward.
            migrationBuilder.Sql(@"
                INSERT INTO FieldRequirementSettings (Id, TenantId, EntityName, FieldName, IsRequired)
                SELECT NEWID(), Id, 'Transaction', 'PayeeId', 0 FROM Tenants;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Payees");

            migrationBuilder.AlterColumn<Guid>(
                name: "PayeeId",
                table: "CompensationTransactions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
