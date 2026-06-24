namespace Wasnie.Application.Common.Options;

/// <summary>
/// Configuration for the Phase-3 HubSpot automatic polling sync. Lives in the "HubSpotSync" config section
/// (appsettings + environment overrides). All knobs are config-only — the cadence can drop from hourly to
/// every 15 minutes with no code change.
/// </summary>
public sealed class HubSpotSyncOptions
{
    public const string SectionName = "HubSpotSync";

    /// <summary>
    /// Master switch for the automatic sync. When false the recurring orchestrator job is REMOVED at
    /// startup (and the orchestrator is a no-op if it somehow still fires). The manual "Import deals"
    /// button keeps working regardless.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Cron expression (5-field, UTC) controlling how often the orchestrator runs. Default = hourly.
    /// To poll every 15 minutes set "*/15 * * * *"; every 30 → "*/30 * * * *". No code change needed.
    /// </summary>
    public string CronExpression { get; init; } = "0 * * * *";

    /// <summary>
    /// Per-tenant fan-out delay in seconds (anti thundering-herd). Tenant at index N is scheduled
    /// N × this many seconds after the orchestrator fires, so 1000 tenants spread across time instead of
    /// hammering the backend/DB at once. 0 = no staggering (all scheduled immediately — not recommended).
    /// </summary>
    public int TenantStaggerSeconds { get; init; } = 5;
}
