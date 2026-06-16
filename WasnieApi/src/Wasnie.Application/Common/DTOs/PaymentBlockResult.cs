namespace Wasnie.Application.Common.DTOs;

public sealed record PaymentBlockResult(
    int TotalConflicts,
    IReadOnlyList<PaymentConflictItem> Conflicts
);

public sealed record PaymentConflictItem(
    string TransactionReference,
    Guid PaidInPayoutId,
    string PaidInPayoutPeriodStart,
    string PaidInPayoutPeriodEnd
);
