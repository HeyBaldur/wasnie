using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B29_PlanArchivedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "CompensationPlans",
                type: "datetimeoffset",
                nullable: true);

            // ── BACKFILL ───────────────────────────────────────────────────────
            // Every plan that is already Archived gets its archive date from UpdatedAt.
            //
            // WHY UpdatedAt AND NOT AuditLogs, which is where this date was assumed to live.
            // For an Archived plan, Archive() is provably the LAST thing that can write UpdatedAt:
            // SetClawbackPolicy refuses when Archived, Activate accepts Draft only (so archiving is
            // terminal and nothing can un-archive), rule mutations are Draft-only, and
            // CloneAsNewVersion stamps the clone, not the source. UpdatedAt is therefore the archive
            // instant, and it is NOT NULL, so no archived plan is missed.
            //
            // AuditLogs is the weaker source, measured rather than assumed (2026-08-31, WasnieDb):
            // AuditBehavior dispatches the entry AFTER the handler without inspecting the Result, and
            // ArchivePlanHandler returns Result.Failure without throwing — so a FAILED archive still
            // writes a PLAN_ARCHIVED row. In the real data one Draft plan carries such a row, and
            // 'Plan Test Flat 5%' carries three rows spanning 3h13m of which only the last matches
            // UpdatedAt. Backfilling from MIN(TimestampUtc) would have dated that plan 3h13m early,
            // on the very field that separates a sale that still pays through the plan from one that
            // does not. AuditLogs is also purgeable, which is the hole this column exists to close.
            //
            // Idempotent (ArchivedAt IS NULL) so a re-run cannot overwrite a real value.
            migrationBuilder.Sql(@"
                UPDATE CompensationPlans
                SET ArchivedAt = UpdatedAt
                WHERE Status = 'Archived' AND ArchivedAt IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "CompensationPlans");
        }
    }
}
