using FluentAssertions;
using Wasnie.Application.Audit.DTOs;
using Wasnie.Application.Audit.Handlers;
using Wasnie.Application.Audit.Queries;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Application.Common.Exceptions;

namespace Wasnie.IntegrationTests.Compensation;

/// <summary>
/// KAN-19 — the audit trail page, against a real SQL Server.
///
/// ★★ THEY RUN THE ACTUAL TRANSLATED QUERY. Every rule worth testing here is a SQL one: a date
/// bound that has to include its own day, an actor filter whose "empty" case would silently mean
/// "everything", and a sort whose tie-break decides whether a row appears on two pages or on none.
/// None of those can be proved in memory (§A2).
/// </summary>
[Collection(CreditAllocationServiceCollection.Name)]
public sealed class AuditLogPageTests(CreditAllocationServiceFixture fixture)
{
    private static AuditLog Entry(
        Guid tenantId,
        string action,
        DateTime whenUtc,
        string actorEmail = "admin@wasnie.test",
        string resourceType = "Plan",
        string? resourceId = null,
        string? before = null,
        string? after = null,
        string? metadata = null) =>
        AuditLog.Create(
            tenantId, whenUtc, "user-1", actorEmail, action, resourceType,
            resourceId ?? Guid.NewGuid().ToString(), "EU Accelerator",
            beforeJson: before, afterJson: after, metadata: metadata);

    private async Task<AuditLogPageDto> RunAsync(Guid tenantId, AuditLogFilter? filter = null)
    {
        await using var db = fixture.CreateDbForTenant(tenantId);
        var handler = new GetAuditLogsHandler(db, new AlwaysAllowAuthorization());
        var result = await handler.Handle(
            new GetAuditLogsQuery(filter ?? new AuditLogFilter(PageSize: 100)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }

    // ══ The date bound ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE MOST OBVIOUS QUERY ANYBODY MAKES OF AN AUDIT TRAIL: "what happened on this day". The
    /// stored value is an instant and the filter is a date, so comparing against midnight would
    /// return the empty set for a day full of activity — and it would look like an answer rather than
    /// like a bug. The upper bound is therefore the start of the NEXT day, exclusive.
    /// </summary>
    [Fact]
    public async Task A_single_day_filter_returns_that_whole_day_not_just_its_midnight()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.AuditLogs.Add(Entry(tenantId, "PLAN_CREATED", new DateTime(2026, 5, 9, 23, 59, 59, DateTimeKind.Utc)));
            db.AuditLogs.Add(Entry(tenantId, "PLAN_ACTIVATED", new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc)));
            db.AuditLogs.Add(Entry(tenantId, "PLAN_ARCHIVED", new DateTime(2026, 5, 10, 13, 45, 0, DateTimeKind.Utc)));
            db.AuditLogs.Add(Entry(tenantId, "PAYEE_CREATED", new DateTime(2026, 5, 10, 23, 59, 59, DateTimeKind.Utc)));
            db.AuditLogs.Add(Entry(tenantId, "QUOTA_CREATED", new DateTime(2026, 5, 11, 0, 0, 1, DateTimeKind.Utc)));
            await db.SaveChangesAsync();
        }

        var day = new DateOnly(2026, 5, 10);
        var page = await RunAsync(tenantId, new AuditLogFilter(From: day, To: day, PageSize: 100));

        page.Items.Select(i => i.Action).Should().BeEquivalentTo(
            ["PLAN_ACTIVATED", "PLAN_ARCHIVED", "PAYEE_CREATED"],
            "the whole day is inside the bound, midnight to 23:59:59");
        page.TotalCount.Should().Be(3, "the count uses the same predicate as the page");
    }

    // ══ The actor filter ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE ROWS A JOB WROTE CARRY NO ACTOR, and asking for them by sending an empty string would
    /// be read as "no filter" — every row would come back while the dropdown claimed to be
    /// filtering. That is the failure the sentinel exists to prevent, and it is asserted against the
    /// database rather than against the handler's shape.
    /// </summary>
    [Fact]
    public async Task The_system_actor_selects_only_the_rows_nobody_performed()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.AuditLogs.Add(Entry(tenantId, "HUBSPOT_TOKEN_REFRESHED", new DateTime(2026, 5, 10, 1, 0, 0, DateTimeKind.Utc), actorEmail: ""));
            db.AuditLogs.Add(Entry(tenantId, "CRM_AUTO_SYNC_COMPLETED", new DateTime(2026, 5, 10, 2, 0, 0, DateTimeKind.Utc), actorEmail: ""));
            db.AuditLogs.Add(Entry(tenantId, "PLAN_ARCHIVED", new DateTime(2026, 5, 10, 3, 0, 0, DateTimeKind.Utc), actorEmail: "admin@wasnie.test"));
            await db.SaveChangesAsync();
        }

        var system = await RunAsync(tenantId, new AuditLogFilter(Actor: "__system__", PageSize: 100));
        system.TotalCount.Should().Be(2);
        system.Items.Should().OnlyContain(i => i.ActorEmail == "");

        var everyone = await RunAsync(tenantId, new AuditLogFilter(PageSize: 100));
        everyone.TotalCount.Should().Be(3, "a null actor is 'anyone', which is a different question");
    }

    /// <summary>
    /// ★ EXACT, NOT A CONTAINS. A substring match would let one person's filter also select another
    /// person's rows — attributing actions to somebody who did not perform them, which on this
    /// screen is the worst outcome available.
    /// </summary>
    [Fact]
    public async Task The_actor_filter_matches_the_whole_address_not_a_substring()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.AuditLogs.Add(Entry(tenantId, "LOGIN_SUCCESS", new DateTime(2026, 5, 10, 1, 0, 0, DateTimeKind.Utc), actorEmail: "ana@wasnie.test"));
            db.AuditLogs.Add(Entry(tenantId, "LOGIN_SUCCESS", new DateTime(2026, 5, 10, 2, 0, 0, DateTimeKind.Utc), actorEmail: "susana@wasnie.test"));
            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId, new AuditLogFilter(Actor: "ana@wasnie.test", PageSize: 100));

        page.TotalCount.Should().Be(1);
        page.Items.Single().ActorEmail.Should().Be("ana@wasnie.test");
    }

    // ══ Paging ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ A BULK WRITE STAMPS MANY ROWS WITH THE SAME INSTANT — the import job writes one per
    /// transaction in a single pass. Sorting on the timestamp alone leaves their relative order to
    /// the database, so a row can appear on two pages or on none, and nothing about the screen would
    /// look wrong. The Id tie-break is what makes paging total.
    /// </summary>
    [Fact]
    public async Task Rows_sharing_one_timestamp_page_without_repeating_or_losing_any()
    {
        var tenantId = Guid.NewGuid();
        var sameInstant = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            for (var i = 0; i < 10; i++)
                db.AuditLogs.Add(Entry(tenantId, "TRANSACTION_INGESTED", sameInstant, resourceId: $"tx-{i}"));
            await db.SaveChangesAsync();
        }

        var first = await RunAsync(tenantId, new AuditLogFilter(Page: 1, PageSize: 4));
        var second = await RunAsync(tenantId, new AuditLogFilter(Page: 2, PageSize: 4));
        var third = await RunAsync(tenantId, new AuditLogFilter(Page: 3, PageSize: 4));

        var seen = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Id).ToList();

        seen.Should().HaveCount(10);
        seen.Should().OnlyHaveUniqueItems("no row may appear on two pages");
        first.TotalCount.Should().Be(10, "and none may be lost");
    }

    // ══ The evidence flag ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★ THE FLAG DECIDES WHETHER A ROW OFFERS A DRAWER AT ALL. If it were true everywhere, the
    /// reader would learn that the drawer is usually empty and stop opening the rows that matter.
    /// </summary>
    [Fact]
    public async Task HasDetail_is_true_only_where_something_was_actually_stored()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.AuditLogs.Add(Entry(tenantId, "LOGIN_SUCCESS", new DateTime(2026, 5, 10, 1, 0, 0, DateTimeKind.Utc), resourceId: "bare"));
            db.AuditLogs.Add(Entry(tenantId, "PLAN_ARCHIVED", new DateTime(2026, 5, 10, 2, 0, 0, DateTimeKind.Utc), resourceId: "with-before", before: "{\"status\":\"Active\"}"));
            db.AuditLogs.Add(Entry(tenantId, "PAYEE_UPDATED", new DateTime(2026, 5, 10, 3, 0, 0, DateTimeKind.Utc), resourceId: "with-meta", metadata: "{\"n\":1}"));
            // An empty string is not evidence — it is a writer that passed "" instead of null.
            db.AuditLogs.Add(Entry(tenantId, "LOGOUT", new DateTime(2026, 5, 10, 4, 0, 0, DateTimeKind.Utc), resourceId: "empty-string", after: ""));
            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId);
        var byResource = page.Items.ToDictionary(i => i.ResourceId, i => i.HasDetail);

        byResource["bare"].Should().BeFalse();
        byResource["with-before"].Should().BeTrue();
        byResource["with-meta"].Should().BeTrue();
        byResource["empty-string"].Should().BeFalse("an empty payload is nothing to show");
    }

    // ══ The filter's vocabulary ══════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE OPTIONS COME FROM THE LOG, NOT FROM <c>AuditActions</c>, and this is the case that
    /// proves why: `transaction_voided` is a literal in VoidTransactionCommand with no constant
    /// anywhere. A dropdown built from the constants would not offer it, and its rows — 18 of them in
    /// the reference tenant, still arriving — would be unreachable by any filter.
    /// </summary>
    [Fact]
    public async Task The_action_options_include_a_code_that_has_no_constant()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.AuditLogs.Add(Entry(tenantId, "transaction_voided", new DateTime(2026, 5, 10, 1, 0, 0, DateTimeKind.Utc)));
            db.AuditLogs.Add(Entry(tenantId, "PLAN_ARCHIVED", new DateTime(2026, 5, 10, 2, 0, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync();
        }

        await using var db2 = fixture.CreateDbForTenant(tenantId);
        var handler = new GetAuditLogFilterOptionsHandler(db2, new AlwaysAllowAuthorization());
        var result = await handler.Handle(new GetAuditLogFilterOptionsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Actions.Should().Contain("transaction_voided");
        result.Value!.Actions.Should().Contain("PLAN_ARCHIVED");
    }

    /// <summary>
    /// ★ THE EMPTY ACTOR IS NOT AN OPTION. An empty entry in a dropdown is unclickable and
    /// unreadable; the screen offers "System" as its own choice instead, carrying the sentinel.
    /// </summary>
    [Fact]
    public async Task The_actor_options_leave_out_the_empty_system_actor()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.AuditLogs.Add(Entry(tenantId, "HUBSPOT_TOKEN_REFRESHED", new DateTime(2026, 5, 10, 1, 0, 0, DateTimeKind.Utc), actorEmail: ""));
            db.AuditLogs.Add(Entry(tenantId, "PLAN_ARCHIVED", new DateTime(2026, 5, 10, 2, 0, 0, DateTimeKind.Utc), actorEmail: "admin@wasnie.test"));
            await db.SaveChangesAsync();
        }

        await using var db2 = fixture.CreateDbForTenant(tenantId);
        var handler = new GetAuditLogFilterOptionsHandler(db2, new AlwaysAllowAuthorization());
        var result = await handler.Handle(new GetAuditLogFilterOptionsQuery(), CancellationToken.None);

        result.Value!.Actors.Should().BeEquivalentTo(["admin@wasnie.test"]);
    }

    // ══ Tenancy and RBAC ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★ ONE TENANT'S TRAIL IS ITS OWN. Enforced by the global query filter, not by this handler —
    /// which is precisely why it is worth an assertion: nothing in the handler would fail review if
    /// the filter were removed.
    /// </summary>
    [Fact]
    public async Task Each_tenant_reads_only_its_own_trail()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(mine))
        {
            db.AuditLogs.Add(Entry(mine, "PLAN_ARCHIVED", new DateTime(2026, 5, 10, 1, 0, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(theirs))
        {
            db.AuditLogs.Add(Entry(theirs, "PAYEE_CREATED", new DateTime(2026, 5, 10, 2, 0, 0, DateTimeKind.Utc)));
            db.AuditLogs.Add(Entry(theirs, "PLAN_CREATED", new DateTime(2026, 5, 10, 3, 0, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync();
        }

        (await RunAsync(mine)).Items.Select(i => i.Action).Should().BeEquivalentTo(["PLAN_ARCHIVED"]);
        (await RunAsync(theirs)).TotalCount.Should().Be(2);
    }

    /// <summary>
    /// ★★ THE GUARD IS Audit.Read, AND IT IS IN THE HANDLER RATHER THAN ON THE CONTROLLER. Hiding the
    /// menu entry is manners; this is the control. A reader without the permission is refused even
    /// when they reach the query by another route.
    /// </summary>
    [Fact]
    public async Task Reading_the_trail_requires_the_audit_permission()
    {
        var tenantId = Guid.NewGuid();
        await using var db = fixture.CreateDbForTenant(tenantId);

        var handler = new GetAuditLogsHandler(db, new DenyAuthorization());

        var act = () => handler.Handle(new GetAuditLogsQuery(new AuditLogFilter()), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain(Permission.AuditRead);
    }

    /// <summary>★ The detail endpoint carries its own guard, not the list's.</summary>
    [Fact]
    public async Task Reading_one_entrys_evidence_requires_the_audit_permission()
    {
        var tenantId = Guid.NewGuid();
        await using var db = fixture.CreateDbForTenant(tenantId);

        var handler = new GetAuditLogDetailHandler(db, new DenyAuthorization());

        var act = () => handler.Handle(new GetAuditLogDetailQuery(1), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    /// <summary>
    /// ★ A MISSING ROW IS A FAILURE, NOT AN EMPTY DETAIL (§B3). Another tenant's id is a miss because
    /// of the global filter; answering it with a blank panel would make "nothing here" and "not
    /// yours" render identically.
    /// </summary>
    [Fact]
    public async Task An_entry_that_is_not_this_tenants_is_reported_as_missing()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        long theirId;

        await using (var db = fixture.CreateDbForTenant(theirs))
        {
            var entry = Entry(theirs, "PLAN_ARCHIVED", new DateTime(2026, 5, 10, 1, 0, 0, DateTimeKind.Utc), before: "{\"a\":1}");
            db.AuditLogs.Add(entry);
            await db.SaveChangesAsync();
            theirId = entry.Id;
        }

        await using var db2 = fixture.CreateDbForTenant(mine);
        var handler = new GetAuditLogDetailHandler(db2, new AlwaysAllowAuthorization());
        var result = await handler.Handle(new GetAuditLogDetailQuery(theirId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse("another tenant's entry must not be readable");
        result.Error.Should().Be("AUDIT.DETAIL_NOT_FOUND");
    }

    private sealed class AlwaysAllowAuthorization : IAuthorizationService
    {
        public Task RequireAsync(string permission, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class DenyAuthorization : IAuthorizationService
    {
        public Task RequireAsync(string permission, CancellationToken ct = default) =>
            throw new ForbiddenException(permission);
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(false);
    }
}
