namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// What the churn trigger did for ONE lost deal. <see cref="Outcome"/> is an explicit vocabulary rather
/// than a bare count because "no debit was written" has several legitimate meanings and an operator who
/// cannot tell them apart cannot tell a working trigger from a silent one.
/// </summary>
public sealed record DealChurnClawbackDto(
    Guid TransactionId,
    string Outcome,
    IReadOnlyList<DealChurnClawbackEntryDto> Entries)
{
    /// <summary>A proportional debit was posted (one per plan that has a maturation window).</summary>
    public const string OutcomeDebited = "Debited";

    /// <summary>This transaction already has its churn debit. Idempotent no-op — the sync re-sees lost deals forever.</summary>
    public const string OutcomeAlreadyPosted = "AlreadyPosted";

    /// <summary>No plan involved has a maturation window configured. The clawback is opt-in and OFF here — not an error.</summary>
    public const string OutcomeNoPolicy = "NoPolicy";

    /// <summary>The deal outlived every maturation window it was credited under: the payee keeps all of it.</summary>
    public const string OutcomeMatured = "Matured";

    /// <summary>Nothing was actually paid out for this transaction, so there is nothing to take back.</summary>
    public const string OutcomeNothingPaid = "NothingPaid";
}

/// <summary>
/// One posted debit, carrying every input the number was computed from. A money figure nobody can
/// recompute is a figure nobody can defend to the person whose pay it reduced.
/// </summary>
public sealed record DealChurnClawbackEntryDto(
    Guid LedgerEntryId,
    Guid PlanId,
    decimal Amount,
    string Currency,
    decimal CommissionPaid,
    int DaysActive,
    int MaturationDays,
    DateOnly EventDate,
    DateTimeOffset PostedAt,
    decimal BalanceAfter);
