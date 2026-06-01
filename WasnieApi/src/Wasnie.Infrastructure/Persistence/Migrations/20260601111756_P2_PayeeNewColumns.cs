using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2_PayeeNewColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmploymentType",
                table: "Payees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Payees",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            // Seed the four new catalog fields for every existing tenant (Optional by default).
            // Email and HireDate were seeded as Required in the previous migration to preserve
            // existing behaviour; Role/ManagerId/EmploymentType/Location default to Optional.
            migrationBuilder.Sql(@"
                INSERT INTO FieldRequirementSettings (Id, TenantId, EntityName, FieldName, IsRequired)
                SELECT NEWID(), Id, 'Payee', 'Role',           0 FROM Tenants;

                INSERT INTO FieldRequirementSettings (Id, TenantId, EntityName, FieldName, IsRequired)
                SELECT NEWID(), Id, 'Payee', 'ManagerId',      0 FROM Tenants;

                INSERT INTO FieldRequirementSettings (Id, TenantId, EntityName, FieldName, IsRequired)
                SELECT NEWID(), Id, 'Payee', 'EmploymentType', 0 FROM Tenants;

                INSERT INTO FieldRequirementSettings (Id, TenantId, EntityName, FieldName, IsRequired)
                SELECT NEWID(), Id, 'Payee', 'Location',       0 FROM Tenants;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmploymentType",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Payees");
        }
    }
}
