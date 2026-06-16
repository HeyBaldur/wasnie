export type PayoutStatus = 'Calculated' | 'Approved' | 'Paid' | 'Disputed';

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
  transactionExternalId: string | null;
  transactionDate: string | null;
  transactionAmount: number | null;
  transactionCurrency: string | null;
  calculation: LineCalculationDto | null;
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
