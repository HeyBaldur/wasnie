/** Mirrors ReconciliationEntryKind on the server. */
export type ReconciliationEntryKind = 'Credit' | 'Transaction' | 'Plan';

/** Mirrors ReconciliationMoneyKind on the server. */
export type ReconciliationMoneyKind = 'None' | 'AffectedBase' | 'Clawback';

export interface ReconciliationRow {
  readonly kind: ReconciliationEntryKind;
  readonly entityId: string;
  /**
   * The transaction the reference belongs to — NOT always `entityId`.
   *
   * ★ On a Credit row the entity is the credit and the reference is its transaction's, so a link
   * built from `entityId` would land on a transaction route holding a credit's id. A link goes where
   * its text says it goes. Null on Plan rows.
   */
  readonly transactionId: string | null;
  readonly referenceNumber: string | null;
  readonly payeeId: string | null;
  readonly payeeName: string | null;
  readonly payeeCode: string | null;
  readonly planId: string | null;
  readonly planName: string | null;
  readonly amount: number | null;
  readonly currency: string | null;
  readonly moneyKind: ReconciliationMoneyKind;
  readonly periodDate: string | null;
  readonly occurredAt: string;
  /** One entry, every reason it has. Never rendered raw — see reconciliation-reason.ts. */
  readonly reasons: readonly string[];
}

/**
 * Money held up, per currency.
 *
 * ★ TWO FIGURES, AND THERE IS NO THIRD. There is deliberately no `net` field: unpaid commission and
 * clawback answer different questions, and a single number would let one hide the other.
 */
export interface ReconciliationCurrencyTotal {
  readonly currency: string;
  readonly affectedBaseAmount: number;
  readonly clawbackAmount: number;
  readonly rowCount: number;
}

export interface ReconciliationReasonCount {
  readonly reason: string;
  readonly count: number;
}

/**
 * ★ COMPUTED BY THE SERVER OVER THE WHOLE FILTERED SET. The cards render these verbatim. Nothing in
 * this feature sums an array of rows to produce a total: the page holds one page of rows, and a card
 * built from it would silently describe less money than the filter actually selected.
 */
export interface ReconciliationSummary {
  readonly totalRows: number;
  readonly byCurrency: readonly ReconciliationCurrencyTotal[];
  readonly byReason: readonly ReconciliationReasonCount[];
}

export interface ReconciliationPage {
  readonly items: readonly ReconciliationRow[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly summary: ReconciliationSummary;
}

export interface ReconciliationFilter {
  readonly payeeId: string | null;
  readonly reason: string | null;
  readonly from: string | null;
  readonly to: string | null;
  readonly page: number;
  readonly pageSize: number;
}

export const EMPTY_RECONCILIATION_FILTER: ReconciliationFilter = {
  payeeId: null,
  reason: null,
  from: null,
  to: null,
  page: 1,
  pageSize: 25,
};

/**
 * What a close request carries: the row, and the person's stated reason. Nothing else.
 *
 * ★★ NO REASON CODES AND NO TIMESTAMPS TRAVEL. Which anomalies this row currently carries, and when
 * each was detected, the SERVER reads from its own queue. A client that could name the fact time
 * would be able to close anomalies that have not happened yet — and the closure is what decides
 * which rows a CFO stops seeing.
 */
export interface CloseReconciliationRowRequest {
  readonly kind: ReconciliationEntryKind;
  readonly entityId: string;
  readonly note: string;
}

/** ★ The reasons come BACK because only the server knew which ones the row carried at that moment. */
export interface CloseReconciliationRowResult {
  readonly entityId: string;
  readonly kind: ReconciliationEntryKind;
  readonly closedReasons: readonly string[];
}
