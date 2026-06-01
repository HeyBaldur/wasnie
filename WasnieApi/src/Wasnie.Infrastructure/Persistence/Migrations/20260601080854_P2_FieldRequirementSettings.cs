using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2_FieldRequirementSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payees_TenantId_Email",
                table: "Payees");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "HireDate",
                table: "Payees",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Payees",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.CreateTable(
                name: "FieldRequirementSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldRequirementSettings", x => x.Id);
                });

            // Deduplicate: if the dev/test DB has rows with the same (TenantId, Email),
            // null out the older duplicates so the filtered unique index can be created.
            // Email is now optional, so null is the correct state for non-unique entries.
            migrationBuilder.Sql(@"
                WITH Dupes AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY TenantId, Email ORDER BY CreatedAt ASC) AS rn
                    FROM Payees
                    WHERE Email IS NOT NULL
                )
                UPDATE Payees SET Email = NULL
                FROM Payees p
                INNER JOIN Dupes d ON p.Id = d.Id
                WHERE d.rn > 1;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Payees_TenantId_Email",
                table: "Payees",
                columns: new[] { "TenantId", "Email" },
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FieldRequirementSettings_TenantId_EntityName_FieldName",
                table: "FieldRequirementSettings",
                columns: new[] { "TenantId", "EntityName", "FieldName" },
                unique: true);

            // Seed existing tenants with Required=true for Email and HireDate so their current
            // validation behaviour is unchanged. New tenants created going forward get Optional
            // defaults, set by the tenant-creation path.
            migrationBuilder.Sql(@"
                INSERT INTO FieldRequirementSettings (Id, TenantId, EntityName, FieldName, IsRequired)
                SELECT NEWID(), Id, 'Payee', 'Email', 1 FROM Tenants;

                INSERT INTO FieldRequirementSettings (Id, TenantId, EntityName, FieldName, IsRequired)
                SELECT NEWID(), Id, 'Payee', 'HireDate', 1 FROM Tenants;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldRequirementSettings");

            migrationBuilder.DropIndex(
                name: "IX_Payees_TenantId_Email",
                table: "Payees");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "HireDate",
                table: "Payees",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Payees",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payees_TenantId_Email",
                table: "Payees",
                columns: new[] { "TenantId", "Email" });
        }
    }
}
