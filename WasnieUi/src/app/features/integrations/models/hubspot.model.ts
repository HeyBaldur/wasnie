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
