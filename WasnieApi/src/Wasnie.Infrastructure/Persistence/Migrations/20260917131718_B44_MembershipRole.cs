using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// KAN-91. The role becomes a property of the MEMBERSHIP, and every existing account gets one.
    ///
    /// ★★ THE BACKFILL IS THE DANGEROUS HALF, NOT THE COLUMN. Until now a person's workspace was the
    /// <c>tenant_id</c> claim and their role was a global Identity role. Sign-in is about to be issued
    /// from <c>TenantUsers</c> instead, so any account without a row here STOPS BEING ABLE TO SIGN IN.
    /// Nobody can repair that from inside the product. The insert below is therefore part of the same
    /// migration as the column — one transaction, both or neither — rather than a script somebody has
    /// to remember to run.
    ///
    /// ★★ IT WAS MEASURED BEFORE IT WAS WRITTEN, and the numbers are why it can be this simple:
    /// 15 users hold a <c>tenant_id</c> claim, 15 hold a role, ZERO hold more than one role, ZERO
    /// point at a tenant that does not exist, and <c>TenantUsers</c> was empty. So the mapping is
    /// one-to-one with no ambiguity to resolve. If any of those numbers had been different this would
    /// have needed a decision rather than an INSERT.
    ///
    /// ★ IT IS IDEMPOTENT AND IT REFUSES TO GUESS. <c>NOT EXISTS</c> keeps it from duplicating a row
    /// KAN-32 already created, and the <c>INNER JOIN</c> on the role means an account with a claim but
    /// no role is SKIPPED rather than given a default. A user silently handed "Rep" — or worse,
    /// "TenantAdmin" — because their role was missing is exactly the kind of quiet wrong answer §B1 is
    /// about. There are none today; the guard is for the day there is one.
    ///
    /// ★ NOTHING IS DELETED. The claims and the Identity roles stay exactly where they are. This
    /// migration only ADDS the truth in its new place, so rolling back is dropping a column.
    /// </summary>
    public partial class B44_MembershipRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "TenantUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Every account that can sign in today becomes a membership of the workspace it is in,
            // carrying the role it already has. See the class remarks for why this is here and not in
            // a script, and for the measurements that make the join unambiguous.
            migrationBuilder.Sql("""
                INSERT INTO TenantUsers (Id, TenantId, UserId, Role, InvitationId, InvitedBy, CreatedAt)
                SELECT
                    NEWID(),
                    t.Id,
                    u.Id,
                    r.Name,
                    NULL,
                    NULL,
                    SYSDATETIMEOFFSET()
                FROM AspNetUsers u
                INNER JOIN AspNetUserClaims c
                        ON c.UserId = u.Id AND c.ClaimType = 'tenant_id'
                INNER JOIN Tenants t
                        ON CAST(t.Id AS NVARCHAR(50)) = c.ClaimValue
                INNER JOIN AspNetUserRoles ur
                        ON ur.UserId = u.Id
                INNER JOIN AspNetRoles r
                        ON r.Id = ur.RoleId
                WHERE NOT EXISTS (
                    SELECT 1 FROM TenantUsers x
                    WHERE x.TenantId = t.Id AND x.UserId = u.Id);
                """);

            // KAN-32 created its rows before this column existed, so they carry the empty default.
            // Those accounts DO have an Identity role — they were created with one — and leaving them
            // blank would lock them out the moment sign-in starts reading this column.
            migrationBuilder.Sql("""
                UPDATE tu
                SET tu.Role = r.Name
                FROM TenantUsers tu
                INNER JOIN AspNetUserRoles ur ON ur.UserId = tu.UserId
                INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
                WHERE tu.Role = N'';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The rows are left in place on purpose: they are memberships, not a cache of the claims,
            // and KAN-32 writes them independently of this column.
            migrationBuilder.DropColumn(
                name: "Role",
                table: "TenantUsers");
        }
    }
}
