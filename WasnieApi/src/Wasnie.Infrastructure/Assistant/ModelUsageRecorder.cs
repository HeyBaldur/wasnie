using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Assistant;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.Infrastructure.Assistant;

/// <summary>
/// The request-scoped <see cref="IModelUsageRecorder"/> (KAN-80). See the interface for why usage is persisted when the
/// request ends rather than by the handlers.
///
/// ★★ ITS OWN DbContext, NOT THE REQUEST'S. By the time the request ends, the request's context may hold a change that
/// failed to save — an assistant row whose insert threw. Saving the usage through that context would retry the failed
/// write as a side effect, and could store an answer the user was told had failed. A fresh context writes the usage rows
/// and nothing else.
///
/// ★★ AND IT COMES FROM A NEW SCOPE, CREATED FROM THE ROOT FACTORY — measured, not assumed. The first version built the
/// context from the request's <c>DbContextOptions</c> inside <see cref="DisposeAsync"/>. That runs WHILE the request scope
/// is being torn down, and EF resolves its logger factory through that scope: every save failed at runtime with
/// "Cannot access a disposed object: IServiceProvider" — while the in-memory unit test, which never touches that path, was
/// green (§A2). A scope created from <see cref="IServiceScopeFactory"/> is independent of the one being disposed.
///
/// ★ WHO AND WHICH ACCOUNT ARE CAPTURED WHEN THE CALL IS RECORDED, not at the end: that is the moment the request's
/// identity is certainly still readable.
/// </summary>
public sealed class ModelUsageRecorder(
    IServiceScopeFactory scopeFactory,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guids,
    ILogger<ModelUsageRecorder> logger) : IModelUsageRecorder, IAsyncDisposable
{
    private readonly ConcurrentQueue<(ModelUsage Usage, Guid TenantId, string UserId, DateTimeOffset At)> _pending = new();
    private Guid? _conversationId;

    public void ForConversation(Guid conversationId) => _conversationId = conversationId;

    public void Record(ModelUsage usage)
    {
        var userId = currentUser.UserId;

        if (!tenantContext.IsResolved || tenantContext.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
        {
            // ★ NEVER SILENT (§B1). A model call with no account to charge it to is a defect somewhere upstream — the
            // assistant refuses unauthenticated requests — and it must be visible rather than quietly uncounted.
            logger.LogError(
                "Model usage for {Call} could not be attributed to an account and was not counted ({PromptTokens} prompt, {CompletionTokens} completion tokens).",
                usage.Call, usage.PromptTokens, usage.CompletionTokens);
            return;
        }

        _pending.Enqueue((usage, tenantContext.TenantId, userId, clock.UtcNowOffset));
    }

    public async ValueTask DisposeAsync()
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        var rows = _pending.ToArray();

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            foreach (var (usage, tenantId, userId, at) in rows)
            {
                db.AssistantTokenUsages.Add(AssistantTokenUsage.Create(
                    guids.NewGuid(), tenantId, userId, _conversationId, usage.Call.ToString(), usage.Model,
                    usage.Upstream, usage.PromptTokens, usage.CompletionTokens, usage.ReasoningTokens, usage.Cost, at));
            }

            // CancellationToken.None: the request is over (possibly because the client left), and the tokens were spent
            // regardless of why it ended.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Losing a count is a defect, not a reason to fail a request that has already answered. Logged with the
            // numbers, so the gap can be reconciled against the provider by hand.
            logger.LogError(
                ex,
                "Model usage could not be saved: {Calls} calls, {PromptTokens} prompt and {CompletionTokens} completion tokens left uncounted.",
                rows.Length,
                rows.Sum(r => r.Usage.PromptTokens ?? 0),
                rows.Sum(r => r.Usage.CompletionTokens ?? 0));
        }
    }
}
