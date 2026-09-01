/**
 * The payee's clawback account, exactly as the backend finished it.
 *
 * MONEY RULE: nothing in this feature recomputes a figure. `retentionApplied` and `amortization`
 * carry the same magnitude with opposite meaning and BOTH arrive from the server — deriving one
 * from the other in the browser would make the client a second source of truth against
 * PayRunSettlement, and the two would drift the first time a rule changed.
 */
export interface PayeeStatement {
  payeeId: string;
  payeeName: string;
  currency: string;

  /**
   * What the payee owes RIGHT NOW: the sum of their whole ledger. Always present, and the number the
   * screen leads with — everything below it describes one past payment, not today.
   */
  currentBalance: number;

  /**
   * The settled pay run: a photograph that never changes. All null when no run has closed against
   * this balance yet — null means "there is no run to describe", which is not the same claim as zero.
   *
   * Cash-flow equation: commissions − retention = net payable (absolute).
   * Balance equation:   previous debt + amortization = new carryover (signed).
   */
  commissionsThisPeriod: number | null;
  retentionApplied: number | null;
  netPayable: number | null;
  previousDebt: number | null;
  amortization: number | null;
  newCarryover: number | null;

  /** Null when the payee's plans in that run used different caps — no single number to name. */
  capPercentApplied: number | null;
  /** True when a cap stopped the debt being collected in full. Drives the extra caption sentence. */
  capLimited: boolean;

  payRunId: string | null;
  settledAt: string | null;
}

export type LedgerOrigin = 'System' | 'Human';

/**
 * Every ledger transaction type, as a runtime array so tests can WALK it instead of restating it.
 * A type added here without its LEDGER.TYPE_* translation leaks the raw key onto the screen, and the
 * spec that catches that needs a list it cannot forget to update — this is that list.
 *
 * Mirrors LedgerTransactionType in the backend; scripts/verify-i18n.mjs fails the build if the two
 * ever drift apart.
 */
export const LEDGER_TRANSACTION_TYPES = [
  'ClawbackDebit',
  'ClawbackForgivenessCredit',
  'ManualBonusCredit',
  'DataCorrectionDebit',
  'ClawbackAppliedCredit',
  'ExternalSettlementCredit',
  'WriteOffCredit',
  'DataCorrectionCredit',
  'FinalSettlementDebit',
] as const;

export type LedgerTransactionType = (typeof LEDGER_TRANSACTION_TYPES)[number];

export interface PayeeLedgerEntry {
  id: string;
  createdAt: string;
  origin: LedgerOrigin;
  transactionType: LedgerTransactionType;
  /** Signed by the server: negative reduces what the payee is owed. */
  amount: number;
  currency: string;
  justification: string;
  createdBy: string;
  sourceExternalDealId: string | null;
  sourceTransactionId: string | null;
  daysActive: number | null;
  maturationDays: number | null;
  sourceCommissionAmount: number | null;
  /** When the deal was actually lost in the CRM (ISO date). Typed, so the table renders it in its own
   *  column instead of reading it out of the justification sentence. Null when no CRM event caused it. */
  eventDate: string | null;
  /** The plan whose clawback policy produced this entry. Null for entries no plan produced. */
  sourcePlanId: string | null;
}

/** The types a human is allowed to write — the engine owns ClawbackDebit and ClawbackAppliedCredit. */
export type ManualAdjustmentType =
  | 'ClawbackForgivenessCredit'
  | 'ManualBonusCredit'
  | 'DataCorrectionDebit'
  | 'DataCorrectionCredit'
  // Closing the account of a payee who has left. Three types, not one, because "recovered through
  // payroll", "the company absorbed it" and "we paid them what we owed" are different facts finance
  // has to report separately.
  | 'ExternalSettlementCredit'
  | 'WriteOffCredit'
  | 'FinalSettlementDebit';

export interface CreateAdjustmentRequest {
  /** Always a POSITIVE magnitude — the sign comes from the type, server-side. */
  transactionType: ManualAdjustmentType;
  amount: number;
  currency: string;
  justification: string;
}

/**
 * One commission credit a departed payee earned that never reached a payout: active (not superseded)
 * and unconsumed. Everything a person needs to act on it, so the queue is work rather than worry.
 */
export interface UnsettledCredit {
  creditId: string;
  amount: number;
  currency: string;
  planName: string;
  ruleName: string;
  allocatedAt: string;
  transactionId: string;
  transactionReference: string;
}

/**
 * One departed payee's open account, in ONE currency. "Open" means either half is non-empty:
 *
 * - `balance` — the ledger balance, signed exactly as the server stored it: negative means they owe
 *   it, positive means Wasnie still owes them.
 * - `unsettledCredits` — commission they EARNED that no payout ever took. This half used to be
 *   invisible: the ledger records what a payee OWES, so unpaid commission leaves no balance row at
 *   all, and a queue built on balances read "nothing outstanding".
 *
 * The two are NEVER added together — they point in opposite directions and come from different
 * sources. `balanceUpdatedAt` is null when there is no balance row at all, which is not the same fact
 * as a balance updated to zero.
 */
export interface TerminatedPayeeBalance {
  payeeId: string;
  payeeName: string;
  employeeCode: string;
  terminationDate: string | null;
  balance: number;
  currency: string;
  balanceUpdatedAt: string | null;
  /**
   * When this account was closed, if it ever was — and if the row is here at all, something arrived
   * AFTER that. Closure is recorded, never used as a filter: the queue keeps deriving membership from
   * money, so a credit that lands after a closure brings the row straight back instead of vanishing.
   */
  accountClosedAt: string | null;
  unsettledCreditTotal: number;
  unsettledCredits: UnsettledCredit[];
}

/**
 * What actually happened to the money. Two values, because the ledger already distinguishes recovering
 * it from absorbing it and a closure that collapsed them would make its own audit trail unanswerable.
 */
export type AccountClosureResolution = 'SettledExternally' | 'WrittenOff';

/**
 * The confirmation payload.
 *
 * ★ IT CARRIES THE EXACT CREDITS THE MODAL SHOWED, with the amounts it showed. The server closes this
 * set or none of it and answers 409 otherwise — comparing totals would not do, because two credits can
 * change while the sum stays put and the set is then a different set.
 */
export interface CloseAccountRequest {
  currency: string;
  resolution: AccountClosureResolution;
  note: string;
  credits: { creditId: string; amount: number }[];
  /** The balance the modal displayed. Null when it displayed no balance row at all. */
  expectedBalance: number | null;
}

export interface CloseAccountResult {
  creditsClosed: number;
  creditTotalClosed: number;
  balanceBefore: number;
  balanceAfter: number;
  currency: string;
}

/** What is outstanding in ONE currency. Never blended across currencies — Wasnie holds no rates. */
export interface TerminatedAccountsTotal {
  currency: string;
  unsettledCreditTotal: number;
  unsettledCreditCount: number;
  payeeCount: number;
}

/** The queue: the rows plus the totals the server computed. No screen adds money. */
export interface TerminatedAccounts {
  rows: TerminatedPayeeBalance[];
  totals: TerminatedAccountsTotal[];
}
