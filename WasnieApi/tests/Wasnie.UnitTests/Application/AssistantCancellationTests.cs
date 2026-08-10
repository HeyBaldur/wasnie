using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.Handlers;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Assistant;
using Wasnie.Domain.Exceptions;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// STOPPING an answer that is being written.
///
/// ★ WHAT THESE TESTS PROTECT. The rest of the assistant lives by "nothing partial is ever stored" —
/// a reply that stops mid-sentence must never be found in a thread looking like a finished answer.
/// Cancelling is the single exception, and it is only safe because of the mark that comes with it. So
/// what is tested here is not "the text is saved" but the pair: the words the user watched arrive are
/// kept, AND the row says the user is the one who ended it.
///
/// ★ AND THE CASE WHERE NOTHING IS KEPT. Stopping before the model has written a word must store
/// nothing at all — an empty bubble is not a shorter answer, and the turn is genuinely unanswered.
/// </summary>
public sealed class AssistantCancellationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private const string AliceId = "user-alice";

    /// <summary>
    /// A provider that writes a few words and then finds the request cancelled — the shape of a user
    /// pressing Stop, where the browser drops the connection and ASP.NET cancels the request token.
    /// </summary>
    private sealed class CancellingProvider(string[] fragments, int cancelAfter) : IChatCompletionProvider
    {
        /// <summary>Cancelled by the fake itself, standing in for the browser hanging up.</summary>
        public CancellationTokenSource Source { get; } = new();

        public bool IsConfigured => true;

        /// <summary>No sections: these tests are about the interruption, not about routing.</summary>
        public Task<string> CompleteJsonAsync(
            IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken) =>
            Task.FromResult("""{"sections":[]}""");

        public Task<AssistantToolRequest?> SelectToolAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<AssistantToolSchema> tools,
            CancellationToken cancellationToken) =>
            Task.FromResult<AssistantToolRequest?>(null);

        public async IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < fragments.Length; i++)
            {
                if (i == cancelAfter)
                {
                    Source.Cancel();
                    // Exactly what the real provider does once the token goes: it stops reading the
                    // vendor's stream rather than finishing the answer nobody is waiting for.
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await Task.Yield();
                yield return fragments[i];
            }
        }
    }

    /// <summary>A provider that answers normally and remembers what it was asked — for the retry.</summary>
    private sealed class RecordingProvider(string[] fragments) : IChatCompletionProvider
    {
        public bool IsConfigured => true;

        public IReadOnlyList<ChatMessage>? Received { get; private set; }

        public Task<string> CompleteJsonAsync(
            IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken) =>
            Task.FromResult("""{"sections":[]}""");

        public Task<AssistantToolRequest?> SelectToolAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<AssistantToolSchema> tools,
            CancellationToken cancellationToken) =>
            Task.FromResult<AssistantToolRequest?>(null);

        public async IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Received = messages;

            foreach (var fragment in fragments)
            {
                await Task.Yield();
                yield return fragment;
            }
        }
    }

    private sealed record Harness(
        ApplicationDbContext Db, StreamAssistantReplyHandler Handler, Guid TenantId);

    private static Harness Build(string dbName, IChatCompletionProvider provider)
    {
        var tenant = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenant);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(AssistantCancellationTests)}.{dbName}")
                .Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(AliceId);

        var entitlement = Substitute.For<IAssistantEntitlement>();
        entitlement.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        entitlement.RequireAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var knowledge = Substitute.For<IAssistantKnowledgeBase>();
        knowledge.Documentation.Returns("Wasnie test documentation.");
        knowledge.IsAvailable.Returns(true);
        knowledge.Sections.Returns([new DocumentationSection("1", "Only section", "Wasnie test documentation.")]);
        knowledge.TableOfContents.Returns("1: Only section");
        knowledge.TextFor(Arg.Any<IEnumerable<string>>()).Returns(string.Empty);

        var navigation = Substitute.For<IUiNavigationMap>();
        navigation.PromptBlock.Returns("/plans | Plans | The list of plans.");
        navigation.IsAvailable.Returns(true);
        navigation.Routes.Returns(["/plans"]);

        var handler = new StreamAssistantReplyHandler(
            db, tenantCtx, user, new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            entitlement, provider, knowledge, navigation,
            new AssistantSectionRouter(provider, knowledge, NullLogger<AssistantSectionRouter>.Instance),
            new AssistantToolRunner(provider, [], NullLogger<AssistantToolRunner>.Instance),
            Options.Create(new GroqOptions { ApiKey = "test-key" }),
            NullLogger<StreamAssistantReplyHandler>.Instance);

        return new Harness(db, handler, tenant);
    }

    private static AssistantConversation SeedConversation(Harness h)
    {
        var conversation = AssistantConversation.Start(Guid.NewGuid(), h.TenantId, AliceId, "Chat", Now);
        h.Db.AssistantConversations.Add(conversation);
        h.Db.SaveChanges();
        return conversation;
    }

    private static async Task<List<AssistantStreamEvent>> DrainAsync(
        Harness h, Guid conversationId, string content, CancellationToken cancellationToken)
    {
        var frames = new List<AssistantStreamEvent>();

        await foreach (var frame in h.Handler.Handle(
            new StreamAssistantReplyCommand(conversationId, content), cancellationToken))
        {
            frames.Add(frame);
        }

        return frames;
    }

    // ── 1. The words that arrived are kept, and marked ────────────────────────

    [Fact]
    public async Task Stopping_mid_answer_stores_what_was_written_as_a_cancelled_turn()
    {
        var provider = new CancellingProvider(["Accelerators ", "pay ", "above quota."], cancelAfter: 2);
        var h = Build(nameof(Stopping_mid_answer_stores_what_was_written_as_a_cancelled_turn), provider);
        var conversation = SeedConversation(h);

        await DrainAsync(h, conversation.Id, "How do accelerators work?", provider.Source.Token);

        var stored = await h.Db.AssistantMessages.IgnoreQueryFilters().OrderBy(m => m.Sequence).ToListAsync();

        stored.Should().HaveCount(2, "the question was committed before the model was called, and the "
            + "words the user watched arrive are kept rather than thrown away");

        stored[0].Role.Should().Be(AssistantMessageRole.User);
        stored[0].Status.Should().Be(AssistantMessageStatus.Complete);

        // Only what had actually been written: the third fragment never left the provider.
        stored[1].Role.Should().Be(AssistantMessageRole.Assistant);
        stored[1].Content.Should().Be("Accelerators pay");

        // ★ THE MARK IS THE POINT. Without it this row is a sentence that stops mid-thought, stored
        // and indistinguishable from an answer the assistant chose to end there.
        stored[1].Status.Should().Be(AssistantMessageStatus.Cancelled);
    }

    [Fact]
    public async Task A_cancelled_reply_leaves_the_thread_answered_so_no_retry_is_offered()
    {
        var provider = new CancellingProvider(["Half an ", "answer."], cancelAfter: 1);
        var h = Build(nameof(A_cancelled_reply_leaves_the_thread_answered_so_no_retry_is_offered), provider);
        var conversation = SeedConversation(h);

        await DrainAsync(h, conversation.Id, "How do accelerators work?", provider.Source.Token);

        var stored = await h.Db.AssistantMessages.IgnoreQueryFilters().ToListAsync();

        // The user ended this turn on purpose. Reporting it as unanswered would put the failure card
        // under it and offer to re-run the very answer they just stopped.
        UnansweredTurn.Exists(stored).Should().BeFalse();
    }

    [Fact]
    public async Task The_status_reaches_the_client_on_every_read()
    {
        var provider = new CancellingProvider(["Half an ", "answer."], cancelAfter: 1);
        var h = Build(nameof(The_status_reaches_the_client_on_every_read), provider);
        var conversation = SeedConversation(h);

        await DrainAsync(h, conversation.Id, "How do accelerators work?", provider.Source.Token);

        var stored = await h.Db.AssistantMessages.IgnoreQueryFilters().OrderBy(m => m.Sequence).ToListAsync();
        var dto = AssistantMapper.ToDto(conversation, stored);

        // ★ THIS IS WHAT SURVIVES THE RELOAD. The panel paints the notice from the row it is given, not
        // from anything the browser remembers about the click.
        dto.Messages[^1].Status.Should().Be(nameof(AssistantMessageStatus.Cancelled));
        dto.Messages[0].Status.Should().Be(nameof(AssistantMessageStatus.Complete));
        dto.LastTurnUnanswered.Should().BeFalse();
    }

    // ── 2. Nothing written yet means nothing stored ───────────────────────────

    [Fact]
    public async Task Stopping_before_the_first_word_stores_no_answer_at_all()
    {
        // Cancelled on the very first fragment: the model had produced nothing a user could have read.
        var provider = new CancellingProvider(["Never arrives."], cancelAfter: 0);
        var h = Build(nameof(Stopping_before_the_first_word_stores_no_answer_at_all), provider);
        var conversation = SeedConversation(h);

        await DrainAsync(h, conversation.Id, "How do accelerators work?", provider.Source.Token);

        var stored = await h.Db.AssistantMessages.IgnoreQueryFilters().ToListAsync();

        // An empty assistant row is not a shorter answer — it is a blank bubble nobody can interpret.
        stored.Should().ContainSingle().Which.Role.Should().Be(AssistantMessageRole.User);

        // And so the turn IS unanswered, which is the truth: the question stands and can be retried.
        UnansweredTurn.Exists(stored).Should().BeTrue();
    }

    // ── 3. Try again, on a turn that was stopped ──────────────────────────────

    /// <summary>Seeds the exact thread a stopped answer leaves behind: a question, then a partial.</summary>
    private static AssistantConversation SeedStoppedTurn(Harness h)
    {
        var conversation = SeedConversation(h);

        h.Db.AssistantMessages.Add(AssistantMessage.Create(
            Guid.NewGuid(), conversation.Id, h.TenantId, AssistantMessageRole.User,
            "How do accelerators work?", 0, Now));

        h.Db.AssistantMessages.Add(AssistantMessage.Create(
            Guid.NewGuid(), conversation.Id, h.TenantId, AssistantMessageRole.Assistant,
            "Accelerators pay", 1, Now, payload: null, status: AssistantMessageStatus.Cancelled));

        h.Db.SaveChanges();
        return conversation;
    }

    [Fact]
    public async Task Try_again_appends_the_new_answer_after_the_stopped_one()
    {
        var provider = new RecordingProvider(["Accelerators ", "pay above quota."]);
        var h = Build(nameof(Try_again_appends_the_new_answer_after_the_stopped_one), provider);
        var conversation = SeedStoppedTurn(h);

        await foreach (var _ in h.Handler.Handle(
            new StreamAssistantReplyCommand(conversation.Id, string.Empty, IsRetry: true),
            CancellationToken.None))
        {
        }

        var stored = await h.Db.AssistantMessages.IgnoreQueryFilters().OrderBy(m => m.Sequence).ToListAsync();

        // ★ THE SLOT MUST BE A FREE ONE. Reusing the question's slot — which is what a failed retry does,
        // because a failure stores no answer — would write over sequence 1, and (ConversationId,
        // Sequence) is UNIQUE: not a wrong thread, a failed save.
        stored.Should().HaveCount(3);
        stored.Select(m => m.Sequence).Should().Equal(0, 1, 2);

        // The question is NOT asked twice: the stored turn is re-answered, not re-sent.
        stored.Count(m => m.Role == AssistantMessageRole.User).Should().Be(1);

        // The stopped fragment is still there, still marked — retrying is not a way to delete it.
        stored[1].Content.Should().Be("Accelerators pay");
        stored[1].Status.Should().Be(AssistantMessageStatus.Cancelled);

        stored[2].Content.Should().Be("Accelerators pay above quota.");
        stored[2].Status.Should().Be(AssistantMessageStatus.Complete);
    }

    [Fact]
    public async Task The_model_never_sees_the_fragment_it_was_stopped_on()
    {
        var provider = new RecordingProvider(["A fresh answer."]);
        var h = Build(nameof(The_model_never_sees_the_fragment_it_was_stopped_on), provider);
        var conversation = SeedStoppedTurn(h);

        await foreach (var _ in h.Handler.Handle(
            new StreamAssistantReplyCommand(conversation.Id, string.Empty, IsRetry: true),
            CancellationToken.None))
        {
        }

        provider.Received.Should().NotBeNull();

        // ★ HANDED BACK ITS OWN TRUNCATED ATTEMPT, THE MODEL CONTINUES FROM THE CUT instead of
        // answering — and the user pressed a button that said "try again". The stopped turn is dropped
        // from the prompt for the same reason the stand-in reply is: it is not something the assistant
        // said.
        provider.Received!.Should().NotContain(m => m.Content == "Accelerators pay");

        // The question itself is still there — that is what is being re-answered.
        provider.Received[^1].Role.Should().Be(ChatMessage.UserRole);
        provider.Received[^1].Content.Should().Be("How do accelerators work?");
    }

    // ── 4. The domain will not accept a cancelled question ────────────────────

    [Fact]
    public void A_user_turn_can_never_be_cancelled()
    {
        // There is no interval in which a question could be stopped — it is typed and sent in one
        // motion — so this state would render "response cancelled" under the user's own words.
        var create = () => AssistantMessage.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AssistantMessageRole.User,
            "How do accelerators work?", 0, Now, payload: null,
            status: AssistantMessageStatus.Cancelled);

        create.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_turn_is_complete_unless_it_is_told_otherwise()
    {
        var message = AssistantMessage.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AssistantMessageRole.Assistant,
            "Accelerators pay above quota.", 1, Now);

        message.Status.Should().Be(AssistantMessageStatus.Complete);
    }
}
