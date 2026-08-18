using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Ledger;

/// <summary>
/// The SECOND half of the BOLA/IDOR closure: quotas, attainment and plan assignments, over HTTP against
/// real SQL Server.
///
/// The ledger WI closed the money endpoints and left these open on purpose (money first). They leak a
/// different currency of the same secret: a quota is a colleague's commission TARGET, attainment is
/// their performance against it, and an assignment is the shape of their compensation.
///
/// ★ TWO REFUSAL SHAPES, AND THAT IS DELIBERATE. Each endpoint must be indistinguishable from ITS OWN
/// not-found answer, not from some project-wide convention:
///   • list-by-payee endpoints answer an unknown payee with an EMPTY PAGE → refusal is an empty page;
///   • by-id endpoints (quota, assignment) answer with 404 → refusal is the same 404 and same message.
/// Picking one shape for both would have made the refusal legible: an empty page where a 404 was
/// expected says "this id is real", which is the whole thing being denied.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PayeeScopedEndpointAuthorizationTests(TestDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private sealed record Seeded(Guid PayeeId, Guid QuotaId, Guid AssignmentId, Guid PlanId);

    /// <summary>A payee with a plan, an assignment and a quota — the three things under test.</summary>
    private async Task<Seeded> SeedAsync(string code, string? ownerUserId = null, Guid? managerId = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantId = TestConstants.TenantA;

        var payee = Payee.Create(tenantId, $"Payee {code}", code, $"{code}@test.com".ToLowerInvariant(),
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        if (ownerUserId is not null) payee.LinkToUser(ownerUserId, "test", Now);
        if (managerId is not null) payee.AssignManager(managerId.Value, "test", Now);
        db.Payees.Add(payee);

        var plan = Plan.Create(tenantId, $"Plan {code}", "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), "EUR",
            "test", Guid.NewGuid(), Now, Guid.NewGuid());
        plan.AddRule("Commission", 1,
            new Measurement
            {
                Type = MeasurementType.Revenue,
                SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum,
            },
            RateTable.Flat(0.10m));
        db.CompensationPlans.Add(plan);

        var quota = Quota.Create(tenantId, payee.Id, plan.Id,
            Money.Of(120_000m, "EUR"),
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            QuotaMeasurementType.Revenue,
            "test", Guid.NewGuid(), Now);
        // Activated on purpose: the attainment endpoint ignores Draft quotas, so a draft one would make
        // the "supervisor still sees it" assertions pass for the wrong reason (empty either way).
        quota.Activate("test", Now, Guid.NewGuid());
        db.Quotas.Add(quota);

        var assignment = PlanAssignment.Create(tenantId, plan.Id, payee.Id,
            PayeeReference.Snapshot(payee.Id, payee.FullName, payee.EmployeeCode),
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "test", Guid.NewGuid(), Now, Guid.NewGuid());
        db.PlanAssignments.Add(assignment);

        await db.SaveChangesAsync();
        return new Seeded(payee.Id, quota.Id, assignment.Id, plan.Id);
    }

    private HttpClient Client(string role, string userId, Guid? tenantId = null) =>
        fixture.Factory.CreateClient().WithAuth(tenantId ?? TestConstants.TenantA, userId, role);

    private static async Task<int> CountAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.GetArrayLength()
            : doc.RootElement.GetProperty("items").GetArrayLength();
    }

    // ══ 1. A rep cannot read a colleague's target, attainment or plan ════════

    [Fact]
    public async Task A_rep_gets_nothing_for_another_payees_quotas_attainment_and_assignments()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        await SeedAsync("SCOPE-OWN-1", repUser);
        var victim = await SeedAsync("SCOPE-VICTIM-1", $"user-{Guid.NewGuid():N}");

        var rep = Client("Rep", repUser);

        (await CountAsync(await rep.GetAsync($"/api/payees/{victim.PayeeId}/quotas")))
            .Should().Be(0, "a colleague's commission target is not the rep's business");
        (await CountAsync(await rep.GetAsync($"/api/payees/{victim.PayeeId}/attainment")))
            .Should().Be(0);
        (await CountAsync(await rep.GetAsync($"/api/payees/{victim.PayeeId}/assignments")))
            .Should().Be(0);

        // The second route onto the SAME handlers — guarding the handler covers both, and this is what
        // proves it rather than assuming it.
        (await CountAsync(await rep.GetAsync($"/api/quotas/payee/{victim.PayeeId}"))).Should().Be(0);
        (await CountAsync(await rep.GetAsync($"/api/assignments/payee/{victim.PayeeId}"))).Should().Be(0);
    }

    /// <summary>
    /// The by-id endpoints never see a payee id in the URL — the owner is discovered from the row. A
    /// per-route check would have sailed straight past them.
    /// </summary>
    [Fact]
    public async Task A_rep_cannot_open_another_payees_quota_or_assignment_by_its_own_id()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        await SeedAsync("SCOPE-OWN-2", repUser);
        var victim = await SeedAsync("SCOPE-VICTIM-2", $"user-{Guid.NewGuid():N}");

        var rep = Client("Rep", repUser);

        var quota = await rep.GetAsync($"/api/quotas/{victim.QuotaId}");
        var assignment = await rep.GetAsync($"/api/assignments/{victim.AssignmentId}");

        quota.StatusCode.Should().Be(HttpStatusCode.NotFound);
        assignment.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // …and identical to a quota/assignment that does not exist at all.
        var imaginaryQuota = await rep.GetAsync($"/api/quotas/{Guid.NewGuid()}");
        var imaginaryAssignment = await rep.GetAsync($"/api/assignments/{Guid.NewGuid()}");

        (await quota.Content.ReadAsStringAsync())
            .Should().Be(await imaginaryQuota.Content.ReadAsStringAsync());
        (await assignment.Content.ReadAsStringAsync())
            .Should().Be(await imaginaryAssignment.Content.ReadAsStringAsync());
    }

    // ══ 2. The global lists are filtered, not refused ════════════════════════

    /// <summary>
    /// `GET /api/quotas` was the widest leak of this batch: no payee id, paged, and SEARCHABLE BY NAME.
    /// A rep did not even need an id — `?search=` handed over any colleague's target.
    /// </summary>
    [Fact]
    public async Task The_tenant_wide_quota_and_assignment_lists_are_filtered_to_the_callers_own()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        var own = await SeedAsync("SCOPE-LIST-OWN", repUser);
        var victim = await SeedAsync("SCOPE-LIST-VICTIM", $"user-{Guid.NewGuid():N}");

        var rep = Client("Rep", repUser);

        var quotas = await (await rep.GetAsync("/api/quotas?page=1&pageSize=100")).Content.ReadAsStringAsync();
        quotas.Should().Contain(own.PayeeId.ToString());
        quotas.Should().NotContain(victim.PayeeId.ToString());

        var assignments = await (await rep.GetAsync("/api/assignments?page=1&pageSize=100")).Content.ReadAsStringAsync();
        assignments.Should().NotContain(victim.PayeeId.ToString());

        // Search is the attack that needed no id at all.
        var searched = await (await rep.GetAsync("/api/quotas?page=1&pageSize=100&search=SCOPE-LIST-VICTIM"))
            .Content.ReadAsStringAsync();
        searched.Should().NotContain(victim.PayeeId.ToString(),
            "searching by a colleague's name must not be a way around the filter");

        // Finance still sees both — the screens these lists feed keep working.
        var finance = await (await Client("CompManager", $"user-{Guid.NewGuid():N}")
            .GetAsync("/api/quotas?page=1&pageSize=100")).Content.ReadAsStringAsync();
        finance.Should().Contain(victim.PayeeId.ToString());
    }

    [Fact]
    public async Task The_roster_of_a_plan_is_filtered_for_a_rep()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        await SeedAsync("SCOPE-PLAN-OWN", repUser);
        var victim = await SeedAsync("SCOPE-PLAN-VICTIM", $"user-{Guid.NewGuid():N}");

        var repBody = await (await Client("Rep", repUser)
            .GetAsync($"/api/assignments/plan/{victim.PlanId}?page=1&pageSize=100")).Content.ReadAsStringAsync();
        var financeBody = await (await Client("CompManager", $"user-{Guid.NewGuid():N}")
            .GetAsync($"/api/assignments/plan/{victim.PlanId}?page=1&pageSize=100")).Content.ReadAsStringAsync();

        repBody.Should().NotContain(victim.PayeeId.ToString());
        financeBody.Should().Contain(victim.PayeeId.ToString());
    }

    // ══ 3. The happy paths still work ════════════════════════════════════════

    [Fact]
    public async Task A_rep_reads_their_own_quota_attainment_and_assignment()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        var own = await SeedAsync("SCOPE-HAPPY", repUser);

        var rep = Client("Rep", repUser);

        (await CountAsync(await rep.GetAsync($"/api/payees/{own.PayeeId}/quotas"))).Should().Be(1);
        (await CountAsync(await rep.GetAsync($"/api/payees/{own.PayeeId}/assignments"))).Should().Be(1);
        (await rep.GetAsync($"/api/quotas/{own.QuotaId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await rep.GetAsync($"/api/assignments/{own.AssignmentId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_manager_reads_their_direct_reports_quota_but_not_a_strangers()
    {
        var managerUser = $"user-{Guid.NewGuid():N}";
        var manager = await SeedAsync("SCOPE-MGR", managerUser);
        var report = await SeedAsync("SCOPE-MGR-REPORT", $"user-{Guid.NewGuid():N}", manager.PayeeId);
        var stranger = await SeedAsync("SCOPE-MGR-STRANGER", $"user-{Guid.NewGuid():N}");

        var client = Client("Manager", managerUser);

        (await CountAsync(await client.GetAsync($"/api/payees/{report.PayeeId}/quotas"))).Should().Be(1);
        (await client.GetAsync($"/api/quotas/{report.QuotaId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await CountAsync(await client.GetAsync($"/api/payees/{stranger.PayeeId}/quotas"))).Should().Be(0);
        (await client.GetAsync($"/api/quotas/{stranger.QuotaId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("TenantAdmin")]
    [InlineData("CompManager")]
    public async Task Supervisory_roles_still_read_every_payee(string role)
    {
        var seeded = await SeedAsync($"SCOPE-SUP-{role}", $"user-{Guid.NewGuid():N}");

        var client = Client(role, $"user-{Guid.NewGuid():N}");

        (await CountAsync(await client.GetAsync($"/api/payees/{seeded.PayeeId}/quotas"))).Should().Be(1);
        (await CountAsync(await client.GetAsync($"/api/payees/{seeded.PayeeId}/attainment"))).Should().Be(1);
        (await client.GetAsync($"/api/quotas/{seeded.QuotaId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ══ 4. Fail-closed and cross-tenant ══════════════════════════════════════

    [Fact]
    public async Task An_unlinked_payee_yields_nothing_to_a_rep()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        await SeedAsync("SCOPE-FC-OWN", repUser);
        var unlinked = await SeedAsync("SCOPE-FC-UNLINKED");

        var rep = Client("Rep", repUser);

        (await CountAsync(await rep.GetAsync($"/api/payees/{unlinked.PayeeId}/quotas"))).Should().Be(0);
        (await rep.GetAsync($"/api/quotas/{unlinked.QuotaId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_rep_of_another_tenant_sees_nothing()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        var seeded = await SeedAsync("SCOPE-XTENANT", repUser);

        // Same user id and role, different tenant claim: without the tenant filter the ownership check
        // itself would match.
        var other = Client("Rep", repUser, TestConstants.TenantB);

        (await CountAsync(await other.GetAsync($"/api/payees/{seeded.PayeeId}/quotas"))).Should().Be(0);
        (await other.GetAsync($"/api/quotas/{seeded.QuotaId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
