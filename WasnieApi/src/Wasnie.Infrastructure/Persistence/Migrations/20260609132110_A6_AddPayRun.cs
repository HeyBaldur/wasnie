using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class A6_AddPayRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PayRunId",
                table: "CompensationPayouts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PayeeCount = table.Column<int>(type: "int", nullable: false),
                    PaidPayeeCount = table.Column<int>(type: "int", nullable: false),
                    ZeroPayoutCount = table.Column<int>(type: "int", nullable: false),
                    TotalAmounts = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationPayouts_PayRunId",
                table: "CompensationPayouts",
                column: "PayRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayRuns_TenantId",
                table: "PayRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PayRuns_Unique",
                table: "PayRuns",
                columns: new[] { "TenantId", "PeriodStart", "PeriodEnd" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CompensationPayouts_PayRuns_PayRunId",
                table: "CompensationPayouts",
                column: "PayRunId",
                principalTable: "PayRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Replace IX_CompensationPayouts_Live.
            // The A4 index covered (TenantId, PayeeId, PlanId, PeriodStart, PeriodEnd) with
            // no PayRunId filter. The new index covers (TenantId, PayRunId, PayeeId, PlanId)
            // and only applies to payouts that belong to a run (PayRunId IS NOT NULL).
            // Orphan payouts (legacy, PayRunId IS NULL) are intentionally excluded — they are
            // pre-A6 records, not constrained by this index.
            //
            // SQL Server does NOT support NOT IN in filtered index predicates.
            // Must use <> AND <> (NOT IN causes CREATE INDEX to fail silently or error).
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS [IX_CompensationPayouts_Live] ON [CompensationPayouts]");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX [IX_CompensationPayouts_Live] " +
                "ON [CompensationPayouts] ([TenantId], [PayRunId], [PayeeId], [PlanId]) " +
                "WHERE [Status] <> 'Paid' AND [Status] <> 'Disputed' AND [PayRunId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the A4 filtered index before removing PayRunId.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS [IX_CompensationPayouts_Live] ON [CompensationPayouts]");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX [IX_CompensationPayouts_Live] " +
                "ON [CompensationPayouts] ([TenantId], [PayeeId], [PlanId], [PeriodStart], [PeriodEnd]) " +
                "WHERE [Status] <> 'Paid' AND [Status] <> 'Disputed'");

            migrationBuilder.DropForeignKey(
                name: "FK_CompensationPayouts_PayRuns_PayRunId",
                table: "CompensationPayouts");

            migrationBuilder.DropTable(
                name: "PayRuns");

            migrationBuilder.DropIndex(
                name: "IX_CompensationPayouts_PayRunId",
                table: "CompensationPayouts");

            migrationBuilder.DropColumn(
                name: "PayRunId",
                table: "CompensationPayouts");
        }
    }
}
