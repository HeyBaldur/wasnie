using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Favorites;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Settings;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-64: favorites are ONE mechanism for every entity type. What must hold: a star persists per user and tenant;
/// starring and un-starring are idempotent; a user may only star what they may see (payees through the access guard,
/// plans through Plans.Read) and a lost access hides the favorite; the limit counts what is visible; and every entity
/// type has a provider.
/// </summary>
public sealed class FavoritesHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUserService _user = Substitute.For<ICurrentUserService>();
    private readonly IAuthorizationService _auth = Substitute.For<IAuthorizationService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IGuidGenerator _guids = Substitute.For<IGuidGenerator>();
    private readonly ApplicationDbContext _db;
    private IPayeeAccessGuard _guard = FakePayeeAccessGuard.SeesEverything();

    public FavoritesHandlerTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _tenant.IsResolved.Returns(true);
        _user.UserId.Returns("user-a");
        _clock.UtcNowOffset.Returns(Now);
        _guids.NewGuid().Returns(_ => Guid.NewGuid());
        _auth.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        _db = NewDb();
    }

    public void Dispose() => _db.Dispose();

    private ApplicationDbContext NewDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options,
        _tenant,
        Substitute.For<MediatR.IPublisher>());

    private FavoriteProviders Providers() => new(
    [
        new PayeeFavoriteProvider(_db, _auth, _guard),
        new PlanFavoriteProvider(_db, _auth),
    ]);

    private Task<Wasnie.Domain.Common.Results.Result> Add(FavoriteEntityType type, Guid id) =>
        new AddFavoriteHandler(_db, _tenant, _user, Providers(), _clock, _guids)
            .Handle(new AddFavoriteCommand(type, id), CancellationToken.None);

    private Task<Wasnie.Domain.Common.Results.Result> Remove(FavoriteEntityType type, Guid id) =>
        new RemoveFavoriteHandler(_db, _tenant, _user)
            .Handle(new RemoveFavoriteCommand(type, id), CancellationToken.None);

    private async Task<IReadOnlyList<FavoriteItemDto>> List(FavoriteEntityType type) =>
        (await new ListFavoritesHandler(_db, _tenant, _user, Providers())
            .Handle(new ListFavoritesQuery(type), CancellationToken.None)).Value!;

    private async Task<Guid> SeedPayee(string name = "Aleksandra Wojcik", string code = "EMP402")
    {
        var payee = Payee.Create(TenantId, name, code, null, null, "seed", Guid.NewGuid(), Now);
        _db.Payees.Add(payee);
        await _db.SaveChangesAsync();
        return payee.Id;
    }

    private async Task<Guid> SeedPlan(string name = "EU Standard Commission 2026")
    {
        var plan = Plan.Create(TenantId, name, "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "seed", Guid.NewGuid(), Now, Guid.NewGuid());
        _db.CompensationPlans.Add(plan);
        await _db.SaveChangesAsync();
        return plan.Id;
    }

    // ── Persistence and idempotence ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_ThenList_ReturnsThePayeeResolvedForDisplay()
    {
        var id = await SeedPayee();

        (await Add(FavoriteEntityType.Payee, id)).IsSuccess.Should().BeTrue();

        var items = await List(FavoriteEntityType.Payee);
        items.Should().ContainSingle();
        items[0].Should().BeEquivalentTo(new FavoriteItemDto(id, "Aleksandra Wojcik", "EMP402", null, "Active"));
    }

    [Fact]
    public async Task Add_ThenList_ReturnsThePlanWithVersionAndNoCode()
    {
        var id = await SeedPlan();

        await Add(FavoriteEntityType.Plan, id);

        var items = await List(FavoriteEntityType.Plan);
        items.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new FavoriteItemDto(id, "EU Standard Commission 2026", null, 1, "Draft"));
    }

    [Fact]
    public async Task Add_Twice_KeepsOneRow()
    {
        var id = await SeedPayee();

        await Add(FavoriteEntityType.Payee, id);
        (await Add(FavoriteEntityType.Payee, id)).IsSuccess.Should().BeTrue();

        (await _db.Favorites.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Remove_DeletesTheRow_AndRemovingAgainIsStillSuccess()
    {
        var id = await SeedPayee();
        await Add(FavoriteEntityType.Payee, id);

        (await Remove(FavoriteEntityType.Payee, id)).IsSuccess.Should().BeTrue();
        (await Remove(FavoriteEntityType.Payee, id)).IsSuccess.Should().BeTrue();

        (await List(FavoriteEntityType.Payee)).Should().BeEmpty();
        (await _db.Favorites.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TheSameId_IsIndependentPerType()
    {
        var id = await SeedPayee();
        await Add(FavoriteEntityType.Payee, id);

        // A plan with that id does not exist, so the plan list stays empty — types never bleed into each other.
        (await List(FavoriteEntityType.Plan)).Should().BeEmpty();
    }

    [Fact]
    public async Task List_IsOrderedByName()
    {
        var zed = await SeedPayee("Zed Nowak", "EMP9");
        var ana = await SeedPayee("Ana Gomez", "EMP1");
        await Add(FavoriteEntityType.Payee, zed);
        await Add(FavoriteEntityType.Payee, ana);

        (await List(FavoriteEntityType.Payee)).Select(i => i.Name).Should().Equal("Ana Gomez", "Zed Nowak");
    }

    // ── Per user ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoUsersOfOneTenant_SeeOnlyTheirOwn()
    {
        var aleksandra = await SeedPayee("Aleksandra Wojcik", "EMP402");
        var andrea = await SeedPayee("Andrea Gomez", "NB-3056");

        await Add(FavoriteEntityType.Payee, aleksandra);
        _user.UserId.Returns("user-b");
        await Add(FavoriteEntityType.Payee, andrea);

        (await List(FavoriteEntityType.Payee)).Select(i => i.EntityId).Should().Equal(andrea);
        _user.UserId.Returns("user-a");
        (await List(FavoriteEntityType.Payee)).Select(i => i.EntityId).Should().Equal(aleksandra);
    }

    [Fact]
    public async Task Remove_NeverTouchesAnotherUsersRow()
    {
        var id = await SeedPayee();
        await Add(FavoriteEntityType.Payee, id);

        _user.UserId.Returns("user-b");
        await Remove(FavoriteEntityType.Payee, id);

        _user.UserId.Returns("user-a");
        (await List(FavoriteEntityType.Payee)).Should().ContainSingle();
    }

    // ── Visibility ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_APayeeTheGuardHides_IsNotFound_AndWritesNothing()
    {
        var mine = await SeedPayee("Me", "EMP1");
        var colleague = await SeedPayee("Colleague", "EMP2");
        _guard = FakePayeeAccessGuard.Sees(mine);

        var result = await Add(FavoriteEntityType.Payee, colleague);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(FavoriteErrors.NotFound);
        (await _db.Favorites.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Add_AnIdThatDoesNotExist_GivesTheSameAnswerAsAHiddenOne()
    {
        (await Add(FavoriteEntityType.Payee, Guid.NewGuid())).Error.Should().Be(FavoriteErrors.NotFound);
        (await Add(FavoriteEntityType.Plan, Guid.NewGuid())).Error.Should().Be(FavoriteErrors.NotFound);
    }

    [Fact]
    public async Task Add_APlanWithoutPlansRead_IsForbidden()
    {
        var id = await SeedPlan();
        _auth.RequireAsync(Permission.PlansRead, Arg.Any<CancellationToken>())
            .Returns(_ => throw new ForbiddenException(Permission.PlansRead));

        var act = () => Add(FavoriteEntityType.Plan, id);

        await act.Should().ThrowAsync<ForbiddenException>();
        (await _db.Favorites.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task List_HidesAFavoriteWhoseAccessWasLost()
    {
        var id = await SeedPayee();
        await Add(FavoriteEntityType.Payee, id);

        _guard = FakePayeeAccessGuard.SeesNothing();

        (await List(FavoriteEntityType.Payee)).Should().BeEmpty();
    }

    [Fact]
    public async Task List_WithoutThePermission_IsEmptyNotAnError()
    {
        var id = await SeedPlan();
        await Add(FavoriteEntityType.Plan, id);
        _auth.HasAsync(Permission.PlansRead, Arg.Any<CancellationToken>()).Returns(false);

        (await List(FavoriteEntityType.Plan)).Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_StillWorksAfterAccessWasLost()
    {
        var id = await SeedPayee();
        await Add(FavoriteEntityType.Payee, id);
        _guard = FakePayeeAccessGuard.SeesNothing();

        (await Remove(FavoriteEntityType.Payee, id)).IsSuccess.Should().BeTrue();
        (await _db.Favorites.CountAsync()).Should().Be(0);
    }

    // ── The limit ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_PastTheLimit_IsRefusedWithTheCode()
    {
        for (var i = 0; i < Favorite.MaxPerType; i++)
            (await Add(FavoriteEntityType.Payee, await SeedPayee($"P{i:00}", $"C{i}"))).IsSuccess.Should().BeTrue();

        var result = await Add(FavoriteEntityType.Payee, await SeedPayee("One too many", "X"));

        result.Error.Should().Be(FavoriteErrors.LimitReached);
        (await _db.Favorites.CountAsync()).Should().Be(Favorite.MaxPerType);
    }

    [Fact]
    public async Task Add_AtTheLimit_ReStarringAnExistingOneStillSucceeds()
    {
        var first = await SeedPayee("P00", "C0");
        await Add(FavoriteEntityType.Payee, first);
        for (var i = 1; i < Favorite.MaxPerType; i++)
            await Add(FavoriteEntityType.Payee, await SeedPayee($"P{i:00}", $"C{i}"));

        (await Add(FavoriteEntityType.Payee, first)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Add_TheLimitCountsOnlyVisibleFavorites()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < Favorite.MaxPerType; i++)
        {
            var id = await SeedPayee($"P{i:00}", $"C{i}");
            ids.Add(id);
            await Add(FavoriteEntityType.Payee, id);
        }

        // One of the fifty is no longer visible: the list shows 49, so there is visibly room for one more.
        var newcomer = await SeedPayee("Newcomer", "NEW");
        _guard = FakePayeeAccessGuard.Sees([.. ids.Skip(1), newcomer]);

        (await Add(FavoriteEntityType.Payee, newcomer)).IsSuccess.Should().BeTrue();
    }

    // ── The extension point ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryEntityType_HasExactlyOneRegisteredProvider()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Wasnie.Application.DependencyInjection.AddApplication(services);

        var providerTypes = services
            .Where(d => d.ServiceType == typeof(IFavoriteEntityProvider))
            .Select(d => d.ImplementationType!)
            .ToList();

        var covered = providerTypes
            .Select(t => t == typeof(PayeeFavoriteProvider) ? FavoriteEntityType.Payee
                : t == typeof(PlanFavoriteProvider) ? FavoriteEntityType.Plan
                : throw new InvalidOperationException($"Unmapped provider {t.Name}: add it to this test."))
            .ToList();

        covered.Should().OnlyHaveUniqueItems();
        covered.Should().BeEquivalentTo(Enum.GetValues<FavoriteEntityType>(),
            "a type without a provider would be silently unusable");
    }
}
