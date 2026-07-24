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
    string? OwnerId,
    IReadOnlyList<CrmLineItem>? LineItems = null)
{
    /// <summary>Line items of the deal (empty when the deal has none, or when they weren't fetched).</summary>
    public IReadOnlyList<CrmLineItem> Lines => LineItems ?? [];
}

/// <summary>
/// A CRM line item of a deal, mapped to a neutral shape. In HubSpot each line item carries its own
/// <c>quantity</c>, <c>price</c> (unit price) and <c>amount</c> (= quantity × price), and a stable
/// <see cref="Id"/> — the key used for per-line-item idempotency.
/// </summary>
public sealed record CrmLineItem(
    string Id,
    string? Name,
    decimal? Quantity,
    decimal? Price,
    decimal? Amount,
    /// <summary>
    /// Product identifier of the line item (HubSpot <c>hs_sku</c>). Null when the line item is not
    /// linked to a catalogued product or the catalogue carries no SKU — normal, not an error.
    /// </summary>
    string? Sku = null,
    /// <summary>
    /// Category value read from the tenant-declared CRM property (WI-CRM-CATEGORY). Null when the tenant
    /// has not configured a property, or the property exists but this line item has no value for it.
    /// Neutral field: the HubSpot source fills it from whichever property the tenant configured.
    /// </summary>
    string? CategoryFromCrm = null);

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
