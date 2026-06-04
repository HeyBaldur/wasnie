export interface CreditListItem {
  id: string;
  transactionId: string;
  referenceNumber: string;
  payeeId: string;
  payeeName: string | null;
  payeeCode: string | null;
  planId: string;
  planName: string;
  ruleName: string;
  originalAmount: number;
  originalCurrency: string;
  creditedAmount: number;
  creditedCurrency: string;
  allocatedAt: string;
  isSuperseded: boolean;
}

export interface CurrencyTotal {
  amount: number;
  currency: string;
}

export interface CreditCounters {
  activeCount: number;
  supersededCount: number;
  activeTotals: CurrencyTotal[];
}

export interface CreditByPayee {
  payeeId: string;
  payeeName: string | null;
  payeeCode: string | null;
  creditCount: number;
  totals: CurrencyTotal[];
  latestAllocatedAt: string;
}

export interface CreditDetail {
  // A — Summary
  id: string;
  isSuperseded: boolean;
  supersededAt: string | null;
  supersededBy: string | null;
  allocatedAt: string;
  allocatedBy: string;
  originalAmount: number;
  originalCurrency: string;
  creditedAmount: number;
  creditedCurrency: string;
  splitPercentage: number;
  role: string;
  // B — Transaction
  transactionId: string;
  referenceNumber: string;
  transactionDate: string;
  transactionAmount: number;
  transactionCurrency: string;
  transactionQuantity: number;
  transactionStatus: string;
  transactionPayeeId: string | null;
  payeeName: string | null;
  payeeCode: string | null;
  // C — Plan & Rule
  planId: string;
  planName: string;
  planVersion: number;
  planStatus: string;
  ruleId: string;
  ruleName: string;
  // D — Snapshot
  rateTableType: string;
  flatRate: number | null;
  ruleSnapshotJson: string;
  triggerAlways: boolean;
  triggerSummary: string | null;
}
