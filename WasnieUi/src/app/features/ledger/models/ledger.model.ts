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

  /** Cash-flow equation: commissions − retention = net payable. Absolute values. */
  commissionsThisPeriod: number;
  retentionApplied: number;
  netPayable: number;

  /** Balance equation: previous debt + amortization = new carryover. Signed. */
  previousDebt: number;
  amortization: number;
  newCarryover: number;

  /** Null when the payee's plans in that run used different caps — no single number to name. */
  capPercentApplied: number | null;
  /** True when a cap stopped the debt being collected in full. Drives the extra caption sentence. */
  capLimited: boolean;

  payRunId: string | null;
  settledAt: string | null;
}

export type LedgerOrigin = 'System' | 'Human';

export type LedgerTransactionType =
  | 'ClawbackDebit'
  | 'ClawbackForgivenessCredit'
  | 'ManualBonusCredit'
  | 'DataCorrectionDebit'
  | 'ClawbackAppliedCredit';

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

/** Only the three types a human is allowed to write — the engine owns the other two. */
export type ManualAdjustmentType =
  | 'ClawbackForgivenessCredit'
  | 'ManualBonusCredit'
  | 'DataCorrectionDebit';

export interface CreateAdjustmentRequest {
  /** Always a POSITIVE magnitude — the sign comes from the type, server-side. */
  transactionType: ManualAdjustmentType;
  amount: number;
  currency: string;
  justification: string;
}
