using Wasnie.Application.Compensation.Calculation;

namespace Wasnie.Application.Models.Calculation;

public sealed record ProcessPendingTransactionsPayload(
    Guid TenantId,
    ProcessPendingScope Scope,
    Guid? ScopeId,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    string TriggeredBy,
    string TriggeredByEmail);
