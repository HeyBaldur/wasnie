export type HubSpotStatus =
  | 'NeverConnected'
  | 'Connected'
  | 'NeedsReconnect'
  | 'Disconnected';

/** Connection status as returned by the backend. Never contains tokens. */
export interface HubSpotConnectionStatus {
  status: HubSpotStatus;
  portalId: number | null;
  statusReason: string | null;
  connectedAt: string | null;
  connectedBy: string | null;
  disconnectedAt: string | null;
  /** When the automatic polling sync last ran successfully (Phase 3). Null = never auto-synced yet. */
  lastSyncedAt: string | null;
  /** WI-CRM-CATEGORY: the HubSpot property the tenant declared feeds Category. Null = not configured. */
  categoryPropertyName: string | null;
  /**
   * True when the workspace is on Free: every HubSpot operation is refused by the API and the hourly
   * auto-sync skips this tenant. Orthogonal to `status` — a tenant that connected while paying and
   * then downgraded reports "Connected" AND this flag, which is the frozen state the card explains.
   */
  requiresUpgrade: boolean;
}

export interface HubSpotConnectResult {
  authorizationUrl: string;
}

export interface HubSpotPingResult {
  portalId: number;
  accountType: string | null;
  timeZone: string | null;
  companyCurrency: string | null;
  uiDomain: string | null;
}
