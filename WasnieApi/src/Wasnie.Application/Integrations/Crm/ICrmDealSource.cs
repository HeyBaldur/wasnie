namespace Wasnie.Application.Integrations.Crm;

/// <summary>
/// Abstraction over a CRM as a source of deals + owners. HubSpot is ONE implementation; Salesforce /
/// Pipedrive would each be another, so the internal pipeline never couples to a specific CRM (per
/// docs/HUBSPOT_INTEGRATION_DESIGN.md "Capa de abstracción").
///
/// READ-ONLY: implementations MUST only read from the CRM. Writing/creating/editing/deleting anything in
/// the CRM is forbidden (the integration is strictly HubSpot → Wasnie).
///
/// Implementations resolve their own credentials for the tenant (e.g. via the HubSpot token store) and
/// throw <see cref="CrmNotConnectedException"/> when there is no usable connection.
/// </summary>
public interface ICrmDealSource
{
    /// <summary>Stable identifier of the CRM this source reads from (e.g. "HubSpot"). Persisted in mappings.</summary>
    string SourceName { get; }

    /// <summary>
    /// Returns every CLOSED-WON deal for the tenant (the deals that generate commission), following the
    /// CRM's cursor pagination internally. Tolerant of custom pipelines/stages — see the implementation
    /// for how "won" is determined. Creates nothing; pure read.
    /// </summary>
    Task<IReadOnlyList<CrmDeal>> GetClosedWonDealsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// INCREMENTAL read (Phase 3 polling): returns CLOSED-WON deals whose last-modified timestamp is at or
    /// after <paramref name="modifiedSince"/>. Brings BOTH brand-new closed-won deals and previously-imported
    /// deals that changed, in one pass — so the polling job sees everything that moved since its last
    /// checkpoint without re-pulling the whole history. Same cursor pagination + rate-limit handling as the
    /// full read; pure read, creates nothing.
    /// </summary>
    Task<IReadOnlyList<CrmDeal>> GetClosedWonDealsModifiedSinceAsync(
        Guid tenantId, DateTimeOffset modifiedSince, CancellationToken cancellationToken = default);

    /// <summary>
    /// REVERSE reconciliation (deal-lost detection): reads the CURRENT won-status of specific deals BY ID,
    /// WITHOUT the closed-won search filter — so it can see a deal that dropped out of closed-won (moved to
    /// Lost or an open stage). Batched internally. A requested id the CRM does not return (deleted/archived/
    /// no access) is omitted from the result, never invented — the caller must treat absence conservatively.
    /// Pure read; creates nothing.
    /// </summary>
    Task<IReadOnlyList<CrmDealStatus>> GetDealStatusesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<string> dealIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the CRM owners for the tenant (used to resolve a deal's owner to a Wasnie payee by email).
    /// Includes archived owners where the CRM exposes them.
    /// </summary>
    Task<IReadOnlyList<CrmOwner>> GetOwnersAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The CRM account's default/company currency (ISO 4217), used as the fallback when a deal carries no
    /// explicit currency code (single-currency accounts don't stamp one per deal). Null if undeterminable.
    /// </summary>
    Task<string?> GetDefaultCurrencyAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown by an <see cref="ICrmDealSource"/> when the tenant has no usable CRM connection (never
/// connected, disconnected, or the saved authorization needs to be renewed). Handlers translate this
/// into a friendly "please reconnect" result rather than a 500.
/// </summary>
public sealed class CrmNotConnectedException(string source)
    : Exception($"{source} is not connected, or the connection needs to be re-authorized.")
{
    public string CrmSource { get; } = source;
}
