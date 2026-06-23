using System.ComponentModel.DataAnnotations;

namespace Wasnie.Application.Common.Models;

public class PaginationQuery
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 25;
    public string? SortBy { get; set; }
    [RegularExpression("^(asc|desc)$", ErrorMessage = "sortOrder must be 'asc' or 'desc'")] public string SortOrder { get; set; } = "asc";
    public string? Search { get; set; }

    // Flat filter params (Format A: ?status=Active&managerId=...&payeeId=...)
    public string? Status { get; set; }
    public Guid? ManagerId { get; set; }
    public Guid? PayeeId { get; set; }
    public string? Source { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }

    // Extended transaction filters (WI-PROD-I.2)
    public string? Reference { get; set; }        // substring match on ReferenceNumber
    public string? Statuses { get; set; }          // comma-separated TransactionStatus values
    public string? PayeeIds { get; set; }          // comma-separated Guid values
    public DateTimeOffset? IngestedFrom { get; set; }
    public DateTimeOffset? IngestedTo { get; set; }
    public decimal? AmountMin { get; set; }
    public decimal? AmountMax { get; set; }
    public bool? UnassignedOnly { get; set; }
    public string? AmountSort { get; set; }        // "asc" | "desc" — sort by Amount
    public string? ReferenceNumbers { get; set; }  // comma-separated exact ReferenceNumber values (for skip-log filter navigation)
    public string? Currencies { get; set; }         // comma-separated ISO 4217 currency codes (WI-PROD-FILTERS-CURRENCY-RULE)
    public string? Period { get; set; }             // "active" (current/future only) | "all" (no period filter)
    public string? AttentionReason { get; set; }     // "NoPayee" | "CurrencyMismatch" | "NoActiveAssignment" — dashboard deep-link to unprocessable Pending
}
