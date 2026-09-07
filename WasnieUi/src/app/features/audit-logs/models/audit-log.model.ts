/** One row of the audit trail. Mirrors AuditLogRowDto. */
export interface AuditLogRow {
  readonly id: number;
  readonly timestampUtc: string;
  /**
   * Empty means the system did it — a background job with nobody signed in.
   *
   * ★ NOT NORMALISED TO A PLACEHOLDER ON THE WIRE. "No human did this" is the fact the log records;
   * turning it into a fake address on the server would make the two cases indistinguishable to
   * anything that reads this contract later.
   */
  readonly actorEmail: string;
  /** A CODE. Never rendered raw — see audit-action.ts. */
  readonly action: string;
  readonly resourceType: string;
  readonly resourceId: string;
  readonly resourceDisplayName: string | null;
  /** Whether opening this row would show anything. Computed by the server over the stored payloads. */
  readonly hasDetail: boolean;
}

/** The evidence behind one row. Mirrors AuditLogDetailDto. */
export interface AuditLogDetail {
  readonly id: number;
  readonly timestampUtc: string;
  readonly actorEmail: string;
  readonly actorUserId: string;
  readonly action: string;
  readonly resourceType: string;
  readonly resourceId: string;
  readonly resourceDisplayName: string | null;
  readonly beforeJson: string | null;
  readonly afterJson: string | null;
  readonly metadata: string | null;
  readonly correlationId: string | null;
  readonly ipAddress: string | null;
  readonly userAgent: string | null;
}

export interface AuditLogPage {
  readonly items: readonly AuditLogRow[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

/**
 * What the dropdowns offer.
 *
 * ★ THE ACTIONS COME FROM THE LOG, NOT FROM A CONSTANT LIST. So the filter can reach rows whose code
 * no longer has (or never had) a constant, and does not offer actions that have never been recorded.
 */
export interface AuditLogFilterOptions {
  readonly actions: readonly string[];
  readonly actors: readonly string[];
}

export interface AuditLogFilter {
  readonly action: string | null;
  /**
   * An exact actor email, or {@link SYSTEM_ACTOR} for "nobody — a job did this".
   *
   * ★★ "ANY ACTOR" AND "THE SYSTEM" ARE DIFFERENT QUESTIONS AND MUST STAY DIFFERENT (§B3), which is
   * exactly why this is a sentinel and not the empty string. The rows a job wrote carry an EMPTY
   * ActorEmail, so the obvious encoding — send `''` — is indistinguishable from "no filter" at three
   * separate layers: the query-string builder drops empty values, `HttpParams` cannot express one,
   * and the server's own `IsNullOrWhiteSpace` guard reads it as absent. All three would silently
   * return every row while the dropdown claimed to be filtering, so the intent travels as a word.
   */
  readonly actor: string | null;
  readonly from: string | null;
  readonly to: string | null;
  readonly page: number;
  readonly pageSize: number;
}

export const EMPTY_AUDIT_LOG_FILTER: AuditLogFilter = {
  action: null,
  actor: null,
  from: null,
  to: null,
  page: 1,
  pageSize: 25,
};

/**
 * The sentinel the "System" dropdown option carries.
 *
 * ★ IT IS NOT A VALID EMAIL, AND THAT IS THE REQUIREMENT. Any string that could also be somebody's
 * address would let a real actor be shadowed by the sentinel. The server matches it verbatim — see
 * GetAuditLogsHandler.Filtered — and never stores it.
 */
export const SYSTEM_ACTOR = '__system__';
