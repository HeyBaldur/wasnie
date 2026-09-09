export interface CurrencyTotal {
  amount: number;
  currency: string;
}

export interface PlanPendingItem {
  planId: string;
  planName: string;
  currency: string;
  pendingCount: number;
}

/** A reason a Pending transaction can't be processed yet. `currencies` is set only for CurrencyMismatch. */
export interface UnprocessablePendingItem {
  reason: 'NoPayee' | 'CurrencyMismatch' | 'NoActiveAssignment';
  count: number;
  currencies: string[];
}

/**
 * A payee whose transactions are blocked because their plan can't be determined: the payee has 2+
 * eligible plans and nobody said which applies, so the engine refuses to guess.
 *
 * Grouped by payee because that's the unit of the FIX — one payee's overlapping assignments block all
 * of their transactions at once, and resolving the overlap unblocks them together.
 */
export interface AmbiguousAttributionPayee {
  payeeId: string;
  payeeName: string;
  employeeCode: string | null;
  transactionCount: number;
  planNames: string[];
}

/**
 * A CRM drift alert: a HubSpot deal changed (amount and/or close date) AFTER its transaction was already
 * Calculated/Paid — money that already moved, not auto-corrected, flagged for manual review. Different in
 * kind from `UnprocessablePendingItem` (which is "can't process yet"). Deep-link target = `referenceNumber`.
 */
export interface DriftAlertItem {
  transactionId: string;
  referenceNumber: string;
  externalDealId: string;
  transactionStatus: 'Calculated' | 'Paid';
  amountChanged: boolean;
  oldAmount: number;
  oldCurrency: string;
  newAmount: number;
  newCurrency: string;
  dateChanged: boolean;
  oldCloseDate: string;
  newCloseDate: string;
  detectedAt: string;
}

/**
 * A deal-lost alert: a HubSpot deal Wasnie already commissioned is NO LONGER closed-won (moved to Lost or
 * an open stage) after its transaction was Calculated/Paid. Distinct from `DriftAlertItem` (amount/date
 * change on a STILL-won deal). Calculated → the UI offers "Revert commission"; Paid → informational only
 * (clawback of paid money is handled outside for now). `commissionAmount` is what a revert takes back.
 */
export interface DealLostAlertItem {
  transactionId: string;
  referenceNumber: string;
  externalDealId: string;
  /** The commission's status RIGHT NOW (joined from the transaction), not the one recorded when the
   *  loss was detected. This is what decides whether a revert may be offered. */
  transactionStatus: string;
  /** The status when the loss was detected — history, never an action. */
  statusAtDetection: string;
  /** For a paid commission: has the churn clawback already booked the debt? */
  clawbackState: 'NotApplicable' | 'Applied' | 'Pending';
  commissionAmount: number;
  commissionCurrency: string;
  detectedAt: string;
}

/**
 * An ACTIVE plan with no rule left in effect — every rule on it was stopped.
 *
 * The plan keeps ingesting sales and pays nothing on any of them, and nothing else on any screen
 * says so: it still looks Active and its assignments still look fine. Derived by the backend from
 * the rules on every read, never a stored flag.
 */
export interface PlanWithoutLiveRules {
  planId: string;
  planName: string;
  version: number;
  /** When the last live rule was stopped — the moment this plan stopped paying. */
  stoppedAt: string | null;
  /** People still assigned to a plan that pays zero. */
  activeAssignmentCount: number;
}

export interface DashboardActionBand {
  draftPayRunsCount: number;
  payoutsPendingApprovalCount: number;
  payoutsPendingApprovalByCurrency: CurrencyTotal[];
  payoutsApprovedUnpaidByCurrency: CurrencyTotal[];
  pendingByPlanItems: PlanPendingItem[];
  unprocessablePendingItems: UnprocessablePendingItem[];
  driftAlerts: DriftAlertItem[];
  dealLostAlerts: DealLostAlertItem[];
  ambiguousAttributionPayees: AmbiguousAttributionPayee[];
  plansWithoutLiveRules: PlanWithoutLiveRules[];
}

export interface DashboardPeriodBand {
  transactionsCount: number;
  transactionsVolumeByCurrency: CurrencyTotal[];
  payoutsTotalByCurrency: CurrencyTotal[];
  creditsCount: number;
  creditsTotalByCurrency: CurrencyTotal[];
  avgQuotaAttainmentPercent: number | null;
  activePlansCount: number;
  activeQuotasCount: number;
  payeesActiveCount: number;
  payeesInactiveCount: number;
}

export interface DashboardTrendPoint {
  currency: string;
  currentAmount: number;
  priorAmount: number;
  /** Change vs the prior period. ALWAYS null while pacing — a running period has no "change". */
  changePercent: number | null;
  direction: 'up' | 'down' | 'neutral' | 'pacing';
  /**
   * Running periods only: currentAmount as a percentage of the previous period's TOTAL. May exceed 100
   * once the baseline is beaten. Null when the previous total is zero (nothing to pace against).
   */
  pacingPercent?: number | null;
}

export interface DashboardTrendBand {
  commissionTrend: DashboardTrendPoint[];
  /** True when the selected range is still running: show pacing progress, never a change percentage. */
  isPacing: boolean;
  /**
   * The exact windows the two bars cover, so a click on either can drill down to the payouts behind it
   * and the screen can label both in the reader's own locale.
   *
   * The PRIOR window is supplied by the backend and never re-derived here: "the same length,
   * immediately before — except a whole calendar month, which compares against the whole previous
   * month" is one rule, and a second implementation of it in the browser would drift.
   *
   * The period LABELS used to arrive from the server as English prose. They are gone: a free range has
   * no name to translate, and these four dates say everything the labels did (§C1).
   */
  currentFrom: string;
  currentTo: string;
  priorFrom: string;
  priorTo: string;
}

/**
 * Total / Paid / Unpaid commission for the selected range, per currency.
 *
 * MONEY RULE: total = paid + unpaid, to the cent. All three come from ONE server-side query over the
 * same set of credits, so they cannot drift apart.
 *
 * `closedTotalByCurrency` is the remainder that is in NONE of the three: commissions written off or
 * settled outside Wasnie. Neither paid nor still owed — reported separately so the omission is visible
 * rather than money quietly vanishing from the screen.
 */
export interface DashboardCommissionsBand {
  totalByCurrency: CurrencyTotal[];
  paidByCurrency: CurrencyTotal[];
  unpaidByCurrency: CurrencyTotal[];
  closedTotalByCurrency: CurrencyTotal[];
  /**
   * The part of `unpaidByCurrency` that NO pay run can reach: the payee has no active assignment to
   * that plan, so the engine never considers those credits.
   *
   * ★ A SUBSET OF UNPAID, NOT A FOURTH BUCKET — it is deliberately not subtracted. The money IS owed
   * and belongs in the debt figure; this only says how much of that debt the system can act on. One
   * payee showed €391,736 owed of which €6,005 could actually leave through a pay run.
   */
  unreachableTotalByCurrency: CurrencyTotal[];
}

export interface DashboardActivityItem {
  timestampUtc: string;
  /** Empty means a background job did it — no human was signed in. */
  actorEmail: string;
  actorInitials: string;
  /** A CODE. Never rendered raw — translated through the audit-logs whitelist (§C2). */
  action: string;
  resourceType: string;
  /** Needed to link the entry to what it changed; the type decides whether there is a route. */
  resourceId: string;
  resourceDisplayName: string | null;
}

export interface DashboardSummary {
  /** The range the server actually applied, echoed back. ISO yyyy-MM-dd. */
  from: string;
  to: string;
  actionBand: DashboardActionBand;
  periodBand: DashboardPeriodBand;
  commissionsBand: DashboardCommissionsBand;
  trendBand: DashboardTrendBand | null;
  activityFeed: DashboardActivityItem[];
}
