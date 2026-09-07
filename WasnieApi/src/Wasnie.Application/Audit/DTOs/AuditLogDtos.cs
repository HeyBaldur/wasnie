namespace Wasnie.Application.Audit.DTOs;

/// <summary>
/// One row of the audit trail, as the table renders it.
///
/// ★ THE ACTION TRAVELS AS A CODE, NEVER AS A SENTENCE (§C1). `PLAN_ARCHIVED` is what the server
/// knows; which words that becomes, and in which language, is the screen's business. The dashboard
/// widget used to build English prose out of this code in the browser and truncate it to three
/// words — so `PLAN_CLAWBACK_POLICY_CHANGED` reached the user as "plan clawback policy" — and that
/// is the failure this contract exists to make impossible.
///
/// ★ <see cref="ActorEmail"/> MAY BE EMPTY, AND THAT MEANS THE SYSTEM. 260 rows in the reference
/// tenant carry no actor (`HUBSPOT_TOKEN_REFRESHED`, `HUBSPOT_RECONNECTED`) because a background job
/// refreshed a token with nobody signed in. It is not normalised to a fake email here: "no human did
/// this" is the fact, and the screen says so in the reader's language.
/// </summary>
public sealed record AuditLogRowDto(
    long Id,
    DateTime TimestampUtc,
    string ActorEmail,
    string Action,
    string ResourceType,
    string ResourceId,
    string? ResourceDisplayName,
    /// <summary>
    /// Whether this row has anything to show in its detail panel.
    ///
    /// ★ COMPUTED HERE SO THE TABLE NEVER OFFERS AN EMPTY DRAWER. Most rows carry no before/after
    /// and no metadata; a "view detail" affordance on every one of them would teach the reader that
    /// the detail is usually empty, and they would stop opening the rows that do have evidence.
    /// </summary>
    bool HasDetail);

/// <summary>
/// The evidence behind one row: everything the log stored and the table has no space for.
///
/// ★ THE JSON TRAVELS AS TEXT, NOT AS A PARSED OBJECT. The log holds whatever the writer serialised
/// at the time — shapes differ per action and have changed over two years. Parsing it here would
/// mean deciding what to do with the rows that no longer fit a current model, and the honest answer
/// for an audit trail is "show exactly what was recorded".
/// </summary>
public sealed record AuditLogDetailDto(
    long Id,
    DateTime TimestampUtc,
    string ActorEmail,
    string ActorUserId,
    string Action,
    string ResourceType,
    string ResourceId,
    string? ResourceDisplayName,
    string? BeforeJson,
    string? AfterJson,
    string? Metadata,
    string? CorrelationId,
    string? IpAddress,
    string? UserAgent);

public sealed record AuditLogPageDto(
    IReadOnlyList<AuditLogRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// What the filter dropdowns offer.
///
/// ★★ BOTH LISTS ARE READ FROM THE LOG, NOT FROM <c>AuditActions</c>. Two reasons, both measured in
/// the reference tenant (KAN-19 Paso 0):
///
/// 1. The log contains codes that have NO constant. `transaction_voided` (18 rows, still being
///    written today) is a literal in <c>VoidTransactionCommand</c>; a dropdown built from the
///    constants would not offer it, and those rows would be unfilterable.
/// 2. 18 of the 96 constants have never been emitted at all (quotas, assignments, rule add/edit).
///    Offering them would be a filter that always returns nothing — which reads as "this never
///    happened" rather than the truth, "this was never recorded".
///
/// The distinct-scan is over a tenant-filtered, indexed column and returns tens of rows, not
/// thousands; it is not worth a cache that could go stale the moment somebody acts.
/// </summary>
public sealed record AuditLogFilterOptionsDto(
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Actors);

/// <summary>
/// The filter the page and its count share.
///
/// ★ <see cref="From"/> AND <see cref="To"/> ARE WHOLE DAYS IN THE READER'S INTENT. `to` is applied
/// inclusively (see the handler) because a person who types the same date in both boxes means "that
/// day", not "the single instant at midnight".
/// </summary>
public sealed record AuditLogFilter(
    string? Action = null,
    string? Actor = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int PageSize = 25);
