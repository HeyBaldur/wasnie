export type PayoutStatus = 'Calculated' | 'Approved' | 'Paid' | 'Disputed' | 'Discarded';

/** Mirrors PayoutLinePaymentState on the server. Serialised as a string (JsonStringEnumConverter). */
export type PayoutLinePaymentState = 'Unpaid' | 'PaidByThisPayout' | 'PaidByAnotherPayout';

/**
 * What a discard did. ★ The count comes BACK because only the server knew how many of the payout's
 * credits another payout had already paid — the list never carried that.
 */
export interface DiscardPayoutResult {
  readonly payoutId: string;
  readonly payeeName: string;
  readonly amount: number;
  readonly currency: string;
  readonly creditsAlreadyPaidElsewhere: number;
}

export interface PayoutListItem {
  id: string;
  payeeId: string;
  payeeName: string;
  payeeCode: string;
  planId: string;
  planName: string;
  periodStart: string;
  periodEnd: string;
  totalCommissionAmount: number;
  totalCommissionCurrency: string;
  status: PayoutStatus;
  /**
   * When the money actually left. Null unless status is Paid — the list is filtered by this when arriving
   * from the dashboard's cash-flow card, so the column has to be visible for that match to make sense.
   */
  paidAt: string | null;
  calculatedAt: string;
  calculatedBy: string;
  updatedAt: string;
  updatedBy: string;
}

export interface RateTierDto {
  from: number;
  to: number | null;
  rate: number;
}

export interface AttainmentTierDto {
  attainmentFrom: number;
  attainmentTo: number | null;
  rate: number;
}

export interface RateTableDto {
  type: 'Flat' | 'Tiered' | 'AttainmentBased';
  flatRate: number | null;
  /**
   * What the rate applies to — 'TransactionAmount' or 'TransactionQuantity'.
   *
   * ★ WITHOUT IT A RATE CANNOT BE RENDERED. See shared/utils/rate-format: a bare decimal is a
   * percentage against an amount and a price per unit against a quantity, and guessing produced
   * "500% flat" on a statement for a rule paying €5 per unit.
   */
  measurementBase?: string;
  tiers: RateTierDto[] | null;
  attainmentTiers: AttainmentTierDto[] | null;
}

export interface ConditionDto {
  field: string;
  operator: string;
  value: string;
}

export interface TriggerDto {
  isAlways: boolean;
  logicalOperator: string;
  conditions: ConditionDto[];
}

export interface ModifierApplicationDto {
  modifierName: string;
  factorApplied: number;
  amountBefore: number;
  amountBeforeCurrency: string;
  amountAfter: number;
  amountAfterCurrency: string;
}

export interface LineCalculationDto {
  planVersion: number;
  frozenAt: string;
  rateTable: RateTableDto;
  trigger: TriggerDto;
  modifiers: ModifierApplicationDto[];
}

export interface PayoutLine {
  id: string;
  creditId: string;
  ruleId: string;
  ruleName: string;
  baseAmount: number;
  baseCurrency: string;
  commissionAmount: number;
  commissionCurrency: string;
  transactionId: string | null;
  transactionReference: string | null;
  /** Human-readable label of the source sale — display only. */
  transactionDescription: string | null;
  transactionExternalId: string | null;
  transactionDate: string | null;
  transactionAmount: number | null;
  transactionCurrency: string | null;
  calculation: LineCalculationDto | null;

  /**
   * What happened to this line's money.
   *
   * ★★ IT COMES FROM THE SERVER AS A STATE, IT IS NOT DERIVED HERE. The first cut had only the
   * "paid by another payout" id and the screen read its absence as "not paid" — so a payout that had
   * itself paid these credits showed every line as unpaid, contradicting the Transactions list. The
   * three cases are decided once, on the server, where the comparison against the current payout
   * actually lives.
   */
  paymentState: PayoutLinePaymentState;

  /** Which payout paid it, only when that payout is NOT this one. */
  paidInPayoutId: string | null;
  paidInPayoutPeriodStart: string | null;
  paidInPayoutPeriodEnd: string | null;
}

export interface PayoutDetail extends PayoutListItem {
  tenantId: string;
  lines: PayoutLine[];
}

export interface CalculatePayoutsRequest {
  periodStart: string;
  periodEnd: string;
  payeeIdFilter?: string | null;
}

export interface BulkApproveRequest {
  payoutIds: string[];
}

export interface BulkApproveResult {
  approved: number;
  errors: string[];
}

export interface BulkMarkPaidRequest {
  payoutIds: string[];
}

export interface BulkMarkPaidResult {
  paid: number;
  errors: string[];
}

export interface PaymentConflictItem {
  transactionReference: string;
  paidInPayoutId: string;
  paidInPayoutPeriodStart: string;
  paidInPayoutPeriodEnd: string;
}

export interface PaymentBlockResponse {
  blocked: boolean;
  totalConflicts: number;
  conflicts: PaymentConflictItem[];
}

export interface OverlappingPayout {
  id: string;
  periodStart: string;
  periodEnd: string;
  status: string;
  planName: string;
  totalCommissionAmount: number;
  totalCommissionCurrency: string;
}

export interface CalculateJobConflict {
  payeeId: string;
  payeeName: string;
  planId: string;
  periodStart: string;
  periodEnd: string;
  status: string;
}

export interface CalculateJobWarning {
  payeeId: string;
  payeeName: string;
  planId: string;
  periodStart: string;
  periodEnd: string;
  pendingTransactionCount: number;
}

export interface CalculateJobResult {
  payoutsCreated: number;
  conflicts: CalculateJobConflict[];
  warnings: CalculateJobWarning[];
}
