using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendPayeeQuotaAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payees_TenantId_Email",
                table: "Payees");

            // Step 1: Add new Payee columns before dropping old ones (data migration requires both to exist)
            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "Payees",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "HireDate",
                table: "Payees",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<Guid>(
                name: "ManagerId",
                table: "Payees",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Payees",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Payees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TerminationDate",
                table: "Payees",
                type: "date",
                nullable: true);

            // Step 2: Add Quota and PlanAssignment columns
            migrationBuilder.AddColumn<int>(
                name: "MeasurementType",
                table: "Quotas",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Quotas",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "PlanAssignments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Step 3: Migrate existing payee data — preserve all records
            migrationBuilder.Sql(@"
                UPDATE Payees
                SET
                    FullName = LTRIM(RTRIM(ISNULL(FirstName, '') + CASE WHEN ISNULL(LastName, '') <> '' THEN ' ' + LastName ELSE '' END)),
                    Status   = CASE WHEN IsActive = 1 THEN 0 ELSE 2 END,
                    HireDate = CAST(CreatedAt AS DATE)
            ");

            // Step 4: Drop old columns now that data has been migrated
            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "Payees");

            // Step 5: Alter Email column
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Payees",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            // Step 6: Create indexes and FK
            migrationBuilder.CreateIndex(
                name: "IX_Payees_ManagerId",
                table: "Payees",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_Payees_TenantId_Email",
                table: "Payees",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_Payees_TenantId_EmployeeCode",
                table: "Payees",
                columns: new[] { "TenantId", "EmployeeCode" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Payees_Payees_ManagerId",
                table: "Payees",
                column: "ManagerId",
                principalTable: "Payees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payees_Payees_ManagerId",
                table: "Payees");

            migrationBuilder.DropIndex(
                name: "IX_Payees_ManagerId",
                table: "Payees");

            migrationBuilder.DropIndex(
                name: "IX_Payees_TenantId_Email",
                table: "Payees");

            migrationBuilder.DropIndex(
                name: "IX_Payees_TenantId_EmployeeCode",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "MeasurementType",
                table: "Quotas");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Quotas");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "PlanAssignments");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "HireDate",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "ManagerId",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "TerminationDate",
                table: "Payees");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Payees",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Payees",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Payees",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Payees",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Payees_TenantId_Email",
                table: "Payees",
                columns: new[] { "TenantId", "Email" },
                unique: true);
        }
    }
}
