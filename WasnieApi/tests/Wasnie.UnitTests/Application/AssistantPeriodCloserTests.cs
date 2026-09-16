using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Stripe;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Assistant;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-83 — a billing period's overage is written down at rollover, the one moment it is still knowable.
///
/// ★ MONEY TESTS. Closing a period twice charges a customer's paid boost twice; not closing it at all hands them a
/// month of overage for free. Both are ways real money goes missing, and both are below.
/// </summary>
public sealed class AssistantPeriodCloserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static (AssistantPeriodCloser Closer, ApplicationDbContext Db, Guid TenantId) Build(long included)
    {
        var tenantId = Guid.NewGuid();
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            ctx, Substitute.For<MediatR.IPublisher>());

        var closer = new AssistantPeriodCloser(
            db,
            Options.Create(new BillingOptions { IncludedAssistantTokensPerMonth = included }),
            new FakeClock(Now.UtcDateTime),
            NullLogger<AssistantPeriodCloser>.Instance);

        return (closer, db, tenantId);
    }

    private static void SeedUsage(ApplicationDbContext db, Guid tenantId, int tokens, DateTimeOffset at)
    {
        db.AssistantTokenUsages.Add(AssistantTokenUsage.Create(
            Guid.NewGuid(), tenantId, "user-a", Guid.NewGuid(), "Answer", "openai/gpt-oss-120b", "Cerebras",
            tokens, 0, null, null, at));
        db.SaveChanges();
    }

    [Fact]
    public async Task A_period_that_rolled_over_is_closed_with_what_it_spent_past_the_allowance()
    {
        var (closer, db, tenantId) = Build(included: 3_000_000);
        var from = Now.AddDays(-30);
        SeedUsage(db, tenantId, 4_000_000, from.AddDays(5));

        await closer.CloseIfRolledOverAsync(tenantId, from, Now, Now);
        await db.SaveChangesAsync();

        var debit = db.AssistantBoostDebits.Single();
        debit.PeriodStart.Should().Be(from);
        debit.UsedInPeriod.Should().Be(4_000_000);
        debit.Tokens.Should().Be(1_000_000, "only the part past the 3M allowance touches the boost");
    }

    [Fact]
    public async Task The_new_periods_usage_is_not_charged_to_the_old_one()
    {
        // ★ THE WINDOW IS CLOSED AT BOTH ENDS. A sum that ran to "now" would keep growing as the new period was spent,
        // and last month's recorded overage would climb long after the month was over.
        var (closer, db, tenantId) = Build(included: 1_000);
        var from = Now.AddDays(-30);
        var boundary = Now.AddDays(-1);

        SeedUsage(db, tenantId, 1_500, from.AddDays(2));   // inside the closed period
        SeedUsage(db, tenantId, 9_000, Now.AddHours(-2));  // already the new period

        await closer.CloseIfRolledOverAsync(tenantId, from, boundary, boundary);
        await db.SaveChangesAsync();

        var debit = db.AssistantBoostDebits.Single();
        debit.UsedInPeriod.Should().Be(1_500);
        debit.Tokens.Should().Be(500);
    }

    [Fact]
    public async Task The_same_period_announced_again_charges_nothing()
    {
        var (closer, db, tenantId) = Build(included: 1_000);
        var from = Now.AddDays(-30);
        SeedUsage(db, tenantId, 5_000, from.AddDays(1));

        await closer.CloseIfRolledOverAsync(tenantId, from, Now, from);
        await db.SaveChangesAsync();

        db.AssistantBoostDebits.Should().BeEmpty("Stripe redelivers, and the period did not move");
    }

    [Fact]
    public async Task A_period_already_closed_is_not_closed_twice()
    {
        var (closer, db, tenantId) = Build(included: 1_000);
        var from = Now.AddDays(-30);
        SeedUsage(db, tenantId, 5_000, from.AddDays(1));

        await closer.CloseIfRolledOverAsync(tenantId, from, Now, Now);
        await db.SaveChangesAsync();
        await closer.CloseIfRolledOverAsync(tenantId, from, Now, Now);
        await db.SaveChangesAsync();

        db.AssistantBoostDebits.Should().ContainSingle("charging the boost twice for one month is the customer's money");
    }

    [Fact]
    public async Task A_first_period_has_nothing_to_close()
    {
        var (closer, db, tenantId) = Build(included: 1_000);

        await closer.CloseIfRolledOverAsync(tenantId, previousPeriodStart: null, previousPeriodEnd: null, Now);
        await db.SaveChangesAsync();

        db.AssistantBoostDebits.Should().BeEmpty();
    }

    [Fact]
    public async Task A_period_inside_its_allowance_is_still_recorded()
    {
        var (closer, db, tenantId) = Build(included: 1_000_000);
        var from = Now.AddDays(-30);
        SeedUsage(db, tenantId, 400_000, from.AddDays(3));

        await closer.CloseIfRolledOverAsync(tenantId, from, Now, Now);
        await db.SaveChangesAsync();

        var debit = db.AssistantBoostDebits.Single();
        debit.Tokens.Should().Be(0, "zero says the period cost no boost; absence would say it was never closed");
    }

    [Fact]
    public async Task Another_tenants_usage_never_lands_in_this_tenants_period()
    {
        var (closer, db, tenantId) = Build(included: 1_000);
        var from = Now.AddDays(-30);
        SeedUsage(db, Guid.NewGuid(), 9_000, from.AddDays(1));

        await closer.CloseIfRolledOverAsync(tenantId, from, Now, Now);
        await db.SaveChangesAsync();

        db.AssistantBoostDebits.Single().UsedInPeriod.Should().Be(0);
    }
}

/// <summary>KAN-83 — how many tokens a boost pack is worth is read from Stripe, and never guessed.</summary>
public sealed class BoostProductMetadataTests
{
    private static Product WithMetadata(params (string Key, string Value)[] pairs) =>
        new() { Id = "prod_x", Metadata = pairs.ToDictionary(p => p.Key, p => p.Value) };

    [Fact]
    public void A_plain_whole_number_is_the_pack_size()
    {
        StripeBoostService.TryReadTokens(WithMetadata(("tokens", "3000000")), out var tokens).Should().BeTrue();
        tokens.Should().Be(3_000_000);
    }

    [Theory]
    [InlineData("3M")]
    [InlineData("3 000 000")]
    [InlineData("3,000,000")]
    [InlineData("3000000.0")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-3000000")]
    public void Anything_that_is_not_a_positive_whole_number_is_refused(string raw)
    {
        // ★ REFUSED, NOT COERCED. Each of these would parse into *some* number under a laxer rule, and the customer
        // would be credited it. A pack we cannot read is a pack we do not sell.
        StripeBoostService.TryReadTokens(WithMetadata(("tokens", raw)), out var tokens).Should().BeFalse($"'{raw}' is not a token count");
        tokens.Should().Be(0);
    }

    [Fact]
    public void A_product_without_the_key_is_refused()
    {
        StripeBoostService.TryReadTokens(WithMetadata(("size", "3000000")), out _).Should().BeFalse();
        StripeBoostService.TryReadTokens(new Product { Id = "prod_x" }, out _).Should().BeFalse();
    }
}
