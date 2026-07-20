namespace Wasnie.Application.Integrations.Crm;

/// <summary>
/// A CRM deal mapped to a NEUTRAL shape, decoupled from any specific CRM (HubSpot, Salesforce, ...).
/// This is the only deal representation the internal pipeline sees — see <see cref="ICrmDealSource"/>.
/// All fields are nullable except <see cref="Id"/> because CRMs allow incomplete records; the
/// materialization step decides how to handle missing values (e.g. owner-less → Unassigned).
/// </summary>
public sealed record CrmDeal(
    string Id,
    string? Name,
    decimal? Amount,
    string? CurrencyCode,
    DateOnly? CloseDate,
    string? OwnerId);

/// <summary>
/// A CRM owner (the user who owns a deal) mapped to a neutral shape. <see cref="Email"/> may be null
/// (e.g. archived owners) which means it cannot be auto-matched to a payee by email.
/// </summary>
public sealed record CrmOwner(
    string Id,
    string? Email,
    string? FirstName,
    string? LastName,
    bool Archived)
{
    public string DisplayName =>
        string.Join(" ", new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)))
            is { Length: > 0 } name
            ? name
            : (Email ?? Id);
}
