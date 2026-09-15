using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Infrastructure.Assistant;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-80 — the request-scoped recorder: every call's usage is written when the request ends, attributed to the account,
/// the user and the conversation, through its own context.
/// </summary>
public sealed class ModelUsageRecorderTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly DbContextOptions<ApplicationDbContext> _options =
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUserService _user = Substitute.For<ICurrentUserService>();

    public ModelUsageRecorderTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _tenant.IsResolved.Returns(true);
        _user.UserId.Returns("user-a");
    }

    /// <summary>
    /// ★ THE REAL RESOLUTION PATH: the recorder gets its context from a NEW scope of a real service provider, which is what
    /// the first runtime run showed matters — building the context from the request's options failed once the request
    /// scope was being disposed, and a test that handed it options directly could never see that.
    /// </summary>
    private ModelUsageRecorder NewRecorder()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddSingleton(_tenant);
        services.AddSingleton(Substitute.For<MediatR.IPublisher>());
        services.AddScoped<ApplicationDbContext>();
        var root = services.BuildServiceProvider();

        return new ModelUsageRecorder(
            root.GetRequiredService<IServiceScopeFactory>(),
            _tenant, _user, new FakeClock(Now), new SystemGuids(), NullLogger<ModelUsageRecorder>.Instance);
    }

    private ApplicationDbContext Read() => new(_options, _tenant, Substitute.For<MediatR.IPublisher>());

    private sealed class SystemGuids : Wasnie.Application.Common.Abstractions.IGuidGenerator
    {
        public Guid NewGuid() => Guid.NewGuid();
    }

    [Fact]
    public async Task Every_call_of_the_request_is_saved_when_the_request_ends()
    {
        var conversationId = Guid.NewGuid();
        await using (var recorder = NewRecorder())
        {
            recorder.ForConversation(conversationId);
            recorder.Record(new ModelUsage(ModelCall.Router, "openai/gpt-oss-120b", "Cerebras", 736, 64, 47, 0.0003m));
            recorder.Record(new ModelUsage(ModelCall.Dispatcher, "openai/gpt-oss-120b", "Cerebras", 3322, 266, 242, 0.0014m));
            recorder.Record(new ModelUsage(ModelCall.Answer, "openai/gpt-oss-120b", null, null, null, null, null));

            await using var before = Read();
            (await before.AssistantTokenUsages.CountAsync()).Should().Be(0, "nothing is written while the request is running");
        }

        await using var db = Read();
        var rows = await db.AssistantTokenUsages.OrderBy(u => u.Call).ToListAsync();

        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(r => r.TenantId == _tenantId && r.UserId == "user-a" && r.ConversationId == conversationId);
        rows.Single(r => r.Call == "Router").PromptTokens.Should().Be(736);
        rows.Single(r => r.Call == "Dispatcher").CompletionTokens.Should().Be(266);
        rows.Single(r => r.Call == "Answer").PromptTokens.Should().BeNull("a stream stopped before its usage message stays null");
    }

    [Fact]
    public async Task A_request_with_no_model_calls_writes_nothing()
    {
        await using (NewRecorder())
        {
        }

        await using var db = Read();
        (await db.AssistantTokenUsages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_call_that_cannot_be_attributed_to_an_account_is_not_counted_against_anyone()
    {
        _tenant.IsResolved.Returns(false);

        await using (var recorder = NewRecorder())
        {
            recorder.Record(new ModelUsage(ModelCall.Answer, "m", null, 10, 10, null, null));
        }

        await using var db = Read();
        (await db.AssistantTokenUsages.IgnoreQueryFilters().CountAsync()).Should().Be(0,
            "charging an unknown account would be inventing a consumer; the recorder logs it as an error instead");
    }
}
