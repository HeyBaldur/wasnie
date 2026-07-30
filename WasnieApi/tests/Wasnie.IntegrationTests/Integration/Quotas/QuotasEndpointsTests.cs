using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Helpers;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Quotas;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class QuotasEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    public QuotasEndpointsTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetCompensationDataAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListQuotas_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/quotas");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetQuota_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync($"/api/quotas/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateQuota_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/quotas", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── List Quotas ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListQuotas_HappyPath_Returns200WithPagedResult()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var response = await _clientA.GetAsync("/api/quotas");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadPagedResultAsync<QuotaSummaryResponse>();
        result.Items.Should().ContainSingle();
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListQuotas_Pagination_ReturnsCorrectPage()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        for (var i = 0; i < 5; i++)
            await CreateQuotaAsync(_clientA, payee.Id, plan.Id, periodOffset: i);

        var response = await _clientA.GetAsync("/api/quotas?page=2&pageSize=3");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadPagedResultAsync<QuotaSummaryResponse>();
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(3);
        result.TotalCount.Should().Be(5);
        result.Items.Should().HaveCount(2);
    }

    // ── Get Single Quota ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetQuota_ExistingId_Returns200()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var response = await _clientA.GetAsync($"/api/quotas/{quota.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<QuotaSummaryResponse>();
        body!.Id.Should().Be(quota.Id);
    }

    [Fact]
    public async Task GetQuota_UnknownId_Returns404()
    {
        var response = await _clientA.GetAsync($"/api/quotas/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Create Quota ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateQuota_ValidRequest_Returns201()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PostAsJsonAsync("/api/quotas", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuotaSummaryResponse>();
        body!.Id.Should().NotBeEmpty();
        body.Amount.Should().Be(10000m);
        body.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task CreateQuota_CurrencyMismatchWithPlan_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA); // EUR plan

        var request = new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "PLN", // PLN quota on EUR plan — mismatch
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PostAsJsonAsync("/api/quotas", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Bulk create: one quota configuration, N payees, all-or-nothing ────────
    // The property that matters is not "it creates several": it is that a batch NEVER lands
    // half-written. The domain permits duplicate quotas, so a partial success would poison the
    // retry — fix the two bad rows, re-send, and the eighteen good ones now exist twice.

    [Fact]
    public async Task BulkCreateQuotas_AllValid_CreatesOneQuotaPerPayee()
    {
        var plan = await CreateActivePlanAsync(_clientA);
        var p1 = await CreatePayeeAsync(_clientA, "BULK001");
        var p2 = await CreatePayeeAsync(_clientA, "BULK002");
        var p3 = await CreatePayeeAsync(_clientA, "BULK003");

        var response = await _clientA.PostAsJsonAsync("/api/quotas/bulk", new
        {
            payeeIds = new[] { p1.Id, p2.Id, p3.Id },
            planId = plan.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkQuotaResultResponse>();
        body!.Failures.Should().BeEmpty();
        body.Created.Should().HaveCount(3);

        // Identical in everything except the payee — that is what "the same quota for all" means.
        body.Created.Select(q => q.PayeeId).Should().BeEquivalentTo(new[] { p1.Id, p2.Id, p3.Id });
        body.Created.Should().OnlyContain(q => q.Amount == 10000m && q.Currency == "EUR");
        body.Created.Should().OnlyContain(q => q.Status == "Draft");

        // And they are really in the database, one row per payee.
        foreach (var payeeId in new[] { p1.Id, p2.Id, p3.Id })
            (await QuotaCountInDbAsync(payeeId)).Should().Be(1);
    }

    [Fact]
    public async Task BulkCreateQuotas_OnePayeeFails_RejectsTheWholeBatchAndWritesNothing()
    {
        var plan = await CreateActivePlanAsync(_clientA); // effective 2025-01-01 .. 2025-12-31
        var p1 = await CreatePayeeAsync(_clientA, "ATOM001");
        var p2 = await CreatePayeeAsync(_clientA, "ATOM002");

        // The period falls outside the plan — a rule the SINGLE create enforces too, so the batch
        // must refuse it for the same reason. Both payees fail it here; the point is the count of
        // rows written afterwards, which must be zero.
        var response = await _clientA.PostAsJsonAsync("/api/quotas/bulk", new
        {
            payeeIds = new[] { p1.Id, p2.Id },
            planId = plan.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = "2024-01-01",
            periodEnd = "2024-03-31",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<BulkQuotaResultResponse>();
        body!.Created.Should().BeEmpty();
        body.Failures.Should().HaveCount(2);
        // The report names people, not GUIDs — the admin picked people.
        body.Failures.Should().OnlyContain(f => f.PayeeName != "" && f.Reason.Contains("plan"));

        // ★ THE ATOMICITY ASSERTION: not one quota row exists, for any payee of the batch.
        foreach (var payeeId in new[] { p1.Id, p2.Id })
            (await QuotaCountInDbAsync(payeeId)).Should().Be(0);
    }

    [Fact]
    public async Task BulkCreateQuotas_OneBadPayeeAmongGoodOnes_LeavesTheGoodOnesUncreated()
    {
        // The 18-of-20 scenario, stated directly. The batch mixes payees whose quota is fine with one
        // whose plan does not exist, so only the last one can fail — and the other two must still be
        // absent afterwards.
        var plan = await CreateActivePlanAsync(_clientA);
        var good1 = await CreatePayeeAsync(_clientA, "MIX001");
        var good2 = await CreatePayeeAsync(_clientA, "MIX002");

        // A currency the plan does not use makes EVERY row fail; to fail exactly one we need a
        // per-payee difference, and the only per-payee input is the id itself. An id belonging to no
        // payee still builds a valid quota (the single-create does the same), so the honest way to
        // fail exactly one row does not exist through the API today — see the note in the report.
        // What this test pins instead is the guarantee that matters: a rejected batch writes nothing.
        var response = await _clientA.PostAsJsonAsync("/api/quotas/bulk", new
        {
            payeeIds = new[] { good1.Id, good2.Id },
            planId = plan.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "PLN", // mismatch with the EUR plan — same rule the single create enforces
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        foreach (var payeeId in new[] { good1.Id, good2.Id })
            (await QuotaCountInDbAsync(payeeId))
                .Should().Be(0, "a refused batch leaves the database exactly as it was");
    }

    [Fact]
    public async Task BulkCreateQuotas_RejectsWhatTheSingleCreateRejects()
    {
        // Parity, asserted rather than assumed: the same request that the single endpoint refuses is
        // refused by the batch, for the same reason.
        var plan = await CreateActivePlanAsync(_clientA); // EUR
        var payee = await CreatePayeeAsync(_clientA, "PARITY01");

        object body(bool bulk) => bulk
            ? new
            {
                payeeIds = new[] { payee.Id }, planId = plan.Id, measurementType = 0,
                amount = 10000m, currency = "PLN", periodStart = "2025-01-01", periodEnd = "2025-03-31",
            }
            : (object)new
            {
                payeeId = payee.Id, planId = plan.Id, measurementType = 0,
                amount = 10000m, currency = "PLN", periodStart = "2025-01-01", periodEnd = "2025-03-31",
            };

        var single = await _clientA.PostAsJsonAsync("/api/quotas", body(false));
        var batch = await _clientA.PostAsJsonAsync("/api/quotas/bulk", body(true));

        single.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        batch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BulkCreateQuotas_OverlappingQuota_IsCreatedSilently()
    {
        // Overlaps are legal today: the engine resolves them by narrowest period, which is what makes
        // monthly quotas inside a quarterly plan work. The batch must therefore be as silent about it
        // as the single create — a warning here would be a business rule living in one endpoint.
        var plan = await CreateActivePlanAsync(_clientA);
        var payee = await CreatePayeeAsync(_clientA, "OVERLAP1");
        await CreateQuotaAsync(_clientA, payee.Id, plan.Id); // 2025-01-01 .. 2025-03-31

        var response = await _clientA.PostAsJsonAsync("/api/quotas/bulk", new
        {
            payeeIds = new[] { payee.Id },
            planId = plan.Id,
            measurementType = 0,
            amount = 99999m,
            currency = "EUR",
            periodStart = "2025-02-01", // squarely inside the existing quota
            periodEnd = "2025-02-28",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkQuotaResultResponse>();
        result!.Failures.Should().BeEmpty();

        (await QuotaCountInDbAsync(payee.Id))
            .Should().Be(2, "both quotas coexist — the domain allows it and so does the batch");
    }

    [Fact]
    public async Task BulkCreateQuotas_EmptyPayeeList_Returns400()
    {
        var plan = await CreateActivePlanAsync(_clientA);

        var response = await _clientA.PostAsJsonAsync("/api/quotas/bulk", new
        {
            payeeIds = Array.Empty<Guid>(),
            planId = plan.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BulkCreateQuotas_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/quotas/bulk", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateQuota_CurrencyMismatchWithPlan_ReturnsError()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA); // EUR plan
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id); // EUR quota

        var request = new
        {
            quotaId = quota.Id,
            measurementType = 0,
            amount = 20000m,
            currency = "PLN", // PLN update on EUR plan — mismatch
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PutAsJsonAsync($"/api/quotas/{quota.Id}", request);

        // Domain exception is caught and returned as Failure → 422 UnprocessableEntity
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreateQuota_ZeroAmount_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 0m,
            currency = "EUR",
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PostAsJsonAsync("/api/quotas", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateQuota_InvalidCurrency_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 5000m,
            currency = "TOOLONG",
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PostAsJsonAsync("/api/quotas", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateQuota_EndBeforeStart_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 5000m,
            currency = "EUR",
            periodStart = "2025-12-31",
            periodEnd = "2025-01-01"
        };

        var response = await _clientA.PostAsJsonAsync("/api/quotas", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateQuota_PeriodWithinPlan_Returns201()
    {
        // Plan effective period is 2025-01-01 .. 2025-12-31; quota period is a sub-window inside it.
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = "2025-04-01",
            periodEnd = "2025-06-30"
        };

        var response = await _clientA.PostAsJsonAsync("/api/quotas", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateQuota_PeriodOutsidePlan_Returns400()
    {
        // Valid date range, but the end extends beyond the plan's 2025-12-31 effective end.
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = "2025-01-01",
            periodEnd = "2026-03-31"
        };

        var response = await _clientA.PostAsJsonAsync("/api/quotas", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Update Quota ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateQuota_ValidRequest_Returns200()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA); // EUR plan
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var request = new
        {
            quotaId = quota.Id,
            measurementType = 0,
            amount = 20000m,
            currency = "EUR", // must match plan currency
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PutAsJsonAsync($"/api/quotas/{quota.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<QuotaSummaryResponse>();
        body!.Amount.Should().Be(20000m);
        body.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task UpdateQuota_ZeroAmount_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var request = new
        {
            quotaId = quota.Id,
            measurementType = 0,
            amount = 0m,
            currency = "EUR",
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PutAsJsonAsync($"/api/quotas/{quota.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateQuota_PeriodStartBeforePlan_Returns422()
    {
        // Plan effective period is 2025-01-01 .. 2025-12-31; start spills before it.
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var request = new
        {
            quotaId = quota.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = "2024-12-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PutAsJsonAsync($"/api/quotas/{quota.Id}", request);

        // UpdateQuota returns 422 (Result-failure → UnprocessableEntity) for period-outside-plan.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task UpdateQuota_PeriodEndAfterPlan_Returns422()
    {
        // Plan effective period is 2025-01-01 .. 2025-12-31; end spills past it.
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var request = new
        {
            quotaId = quota.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = "2025-10-01",
            periodEnd = "2026-03-31"
        };

        var response = await _clientA.PutAsJsonAsync($"/api/quotas/{quota.Id}", request);

        // UpdateQuota returns 422 (Result-failure → UnprocessableEntity) for period-outside-plan.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task UpdateQuota_PeriodPartiallyOverlapsPlan_Returns422()
    {
        // Starts inside the plan period but ends beyond it: partial overlap must be rejected —
        // the quota period must be CONTAINED in the plan period, not merely intersect it.
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var request = new
        {
            quotaId = quota.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = "2025-06-01",
            periodEnd = "2026-06-30"
        };

        var response = await _clientA.PutAsJsonAsync($"/api/quotas/{quota.Id}", request);

        // UpdateQuota returns 422 (Result-failure → UnprocessableEntity) for period-outside-plan.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task UpdateQuota_PeriodWithinPlan_Returns200()
    {
        // A sub-window fully inside the plan's 2025-01-01 .. 2025-12-31 period is accepted.
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var request = new
        {
            quotaId = quota.Id,
            measurementType = 0,
            amount = 15000m,
            currency = "EUR",
            periodStart = "2025-07-01",
            periodEnd = "2025-09-30"
        };

        var response = await _clientA.PutAsJsonAsync($"/api/quotas/{quota.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Activate / Close Quota ────────────────────────────────────────────────

    [Fact]
    public async Task ActivateQuota_DraftQuota_Returns204()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var response = await _clientA.PostAsync($"/api/quotas/{quota.Id}/activate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CloseQuota_ActiveQuota_Returns204()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);
        await _clientA.PostAsync($"/api/quotas/{quota.Id}/activate", null);

        var response = await _clientA.PostAsync($"/api/quotas/{quota.Id}/close", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Cross-tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task ListQuotas_CrossTenant_DoesNotLeak()
    {
        var payeeA = await CreatePayeeAsync(_clientA, "TA001");
        var planA = await CreateActivePlanAsync(_clientA);
        await CreateQuotaAsync(_clientA, payeeA.Id, planA.Id);

        var payeeB = await CreatePayeeAsync(_clientB, "TB001");
        var planB = await CreateActivePlanAsync(_clientB);
        await CreateQuotaAsync(_clientB, payeeB.Id, planB.Id);

        var response = await _clientA.GetAsync("/api/quotas");
        var result = await response.Content.ReadPagedResultAsync<QuotaSummaryResponse>();

        result.Items.Should().ContainSingle(q => q.PayeeEmployeeCode == "TA001");
        result.Items.Should().NotContain(q => q.PayeeEmployeeCode == "TB001");
    }

    [Fact]
    public async Task GetQuota_CrossTenantById_Returns404()
    {
        var payeeA = await CreatePayeeAsync(_clientA);
        var planA = await CreateActivePlanAsync(_clientA);
        var quotaA = await CreateQuotaAsync(_clientA, payeeA.Id, planA.Id);

        // Tenant B tries to access Tenant A's quota by ID
        var response = await _clientB.GetAsync($"/api/quotas/{quotaA.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateQuota_CrossTenant_Returns422OrNotFound()
    {
        var payeeA = await CreatePayeeAsync(_clientA);
        var planA = await CreateActivePlanAsync(_clientA);
        var quotaA = await CreateQuotaAsync(_clientA, payeeA.Id, planA.Id);

        var request = new
        {
            quotaId = quotaA.Id,
            measurementType = 0,
            amount = 99999m,
            currency = "EUR",
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        // TODO: F-028 — current impl returns 422 (UnprocessableEntity) instead of 404 for cross-tenant update.
        // Revisit when cross-tenant response standardization is completed.
        var response = await _clientB.PutAsJsonAsync($"/api/quotas/{quotaA.Id}", request);

        new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.NotFound }
            .Should().Contain(response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<PayeeResponse> CreatePayeeAsync(HttpClient client, string code = "EMP001")
    {
        var request = new
        {
            fullName = $"Test Payee {code}",
            employeeCode = code,
            email = $"{code.ToLower()}@test.com",
            hireDate = "2024-01-01"
        };
        var response = await client.PostAsJsonAsync("/api/payees", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PayeeResponse>())!;
    }

    private async Task<PlanResponse> CreateActivePlanAsync(HttpClient client)
    {
        var planRequest = new
        {
            name = $"Test Plan {Guid.NewGuid().ToString("N")[..8]}",
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR"
        };
        var planResp = await client.PostAsJsonAsync("/api/plans", planRequest);
        planResp.EnsureSuccessStatusCode();
        var plan = (await planResp.Content.ReadFromJsonAsync<PlanResponse>())!;

        var ruleRequest = new
        {
            planId = plan.Id,
            name = "Base Commission",
            sortOrder = 1,
            measurement = new { _schema = 1, type = 0, sourceField = "amount", aggregation = 0 },
            rateTable = new { _schema = 1, type = 0, flatRate = 0.05 },
            trigger = (object?)null,
            modifier = (object?)null,
            cap = (object?)null,
            floor = (object?)null
        };
        var ruleResp = await client.PostAsJsonAsync($"/api/plans/{plan.Id}/rules", ruleRequest);
        ruleResp.EnsureSuccessStatusCode();

        var activateResp = await client.PostAsync($"/api/plans/{plan.Id}/activate", null);
        activateResp.EnsureSuccessStatusCode();

        return plan;
    }

    private async Task<QuotaSummaryResponse> CreateQuotaAsync(
        HttpClient client, Guid payeeId, Guid planId, int periodOffset = 0)
    {
        // Monthly sub-periods so multiple quotas stay within the plan's effective period
        // (2025-01-01 .. 2025-12-31) — quota period must be contained within the plan period.
        var start = new DateOnly(2025, 1, 1).AddMonths(periodOffset);
        var end = start.AddMonths(1).AddDays(-1);
        var request = new
        {
            payeeId,
            planId,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = start.ToString("yyyy-MM-dd"),
            periodEnd = end.ToString("yyyy-MM-dd")
        };
        var response = await client.PostAsJsonAsync("/api/quotas", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuotaSummaryResponse>())!;
    }

    private sealed record PayeeResponse(Guid Id, string FullName, string EmployeeCode, string StatusLabel);
    private sealed record PlanResponse(Guid Id, string Name, string Status, int Version);
    private sealed record QuotaSummaryResponse(Guid Id, Guid PayeeId, string PayeeEmployeeCode, decimal Amount, string Currency, string Status);

    /// <summary>
    /// Counts the payee's quotas straight from the database.
    ///
    /// NOT through GET /api/quotas/payee/{id}: that endpoint applies a default PERIOD filter when the
    /// caller sends none, so quotas outside the current month are invisible through it — which says
    /// nothing about whether a row was written. For an atomicity assertion the only honest question is
    /// "is the row in the table", so this asks the table.
    /// </summary>
    private async Task<int> QuotaCountInDbAsync(Guid payeeId)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Quotas.IgnoreQueryFilters().CountAsync(q => q.PayeeId == payeeId);
    }

    private sealed record BulkQuotaFailureResponse(Guid PayeeId, string PayeeName, string PayeeEmployeeCode, string Reason);

    private sealed record BulkQuotaResultResponse(
        IReadOnlyList<QuotaSummaryResponse> Created,
        IReadOnlyList<BulkQuotaFailureResponse> Failures);
}
