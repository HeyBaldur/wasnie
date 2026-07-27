import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { Transaction, CreateTransactionRequest, AssignPayeeRequest, ReassignPayeeRequest, VoidTransactionRequest, PlanOptions } from '../models/transaction.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';
import { buildHttpParams } from '../../../shared/utils/build-http-params';
import { JobState } from '../../imports/transactions/models/transaction-import.models';

export type ProcessPendingScope = 'ByPlanAssignment' | 'ByPlan' | 'ByPayeeAndPeriod';

export interface BulkVoidRequest {
  transactionIds: string[];
  reason: string;
}

export interface BulkVoidResult {
  voidedCount: number;
  errors: string[];
}

export interface ProcessPendingRequest {
  scope: ProcessPendingScope;
  scopeId?: string | null;
  periodStart?: string | null;
  periodEnd?: string | null;
}

export interface ProcessPendingResponse {
  jobId: string;
  candidateCount: number;
}

export interface EligiblePendingTransaction {
  id: string;
  payeeId: string | null;
  referenceNumber: string;
  payeeName: string | null;
  payeeCode: string | null;
  transactionDate: string;
  amount: number;
  currency: string;
}

export interface EligiblePendingResult {
  transactions: EligiblePendingTransaction[];
  totalCount: number;
}

export interface ProcessPendingResultSummary {
  processed: number;
  creditsCreated: number;
  skippedByOverlapRule: number;
  skippedByIdempotency: number;
  skippedByValidation: number;
  skipReasonCounts: Record<string, number>;
  skipDetails: {
    txId: string;
    refNum: string;
    txDate: string;
    amount: number;
    currency: string;
    payeeName: string | null;
    payeeCode: string | null;
    reason: string;
  }[];
}

export interface JobStatus {
  id: string;
  state: JobState;
  progressCurrent: number;
  progressTotal: number;
  errorMessage: string | null;
  enqueuedAtUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  resultSummary: string | null;
}

@Injectable({ providedIn: 'root' })
export class TransactionsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/transactions';

  list(params?: PaginationParams): Observable<PagedResult<Transaction>> {
    return this.http.get<PagedResult<Transaction>>(this.base, { params: buildHttpParams(params) });
  }

  getById(id: string): Observable<Transaction> {
    return this.http.get<Transaction>(`${this.base}/${id}`);
  }

  create(request: CreateTransactionRequest): Observable<Transaction> {
    return this.http.post<Transaction>(this.base, request);
  }

  /**
   * Plans this payee could be credited under on this date/currency. The server also says whether a
   * choice is mandatory, so the form never re-derives the engine's eligibility rule itself.
   */
  getPlanOptions(payeeId: string, transactionDate: string, currency: string): Observable<PlanOptions> {
    return this.http.get<PlanOptions>(`${this.base}/plan-options`, {
      params: { payeeId, transactionDate, currency },
    });
  }

  assignPayee(transactionId: string, request: AssignPayeeRequest): Observable<Transaction> {
    return this.http.post<Transaction>(`${this.base}/${transactionId}/assign-payee`, request);
  }

  reassignPayee(transactionId: string, request: ReassignPayeeRequest): Observable<Transaction> {
    return this.http.post<Transaction>(`${this.base}/${transactionId}/reassign-payee`, request);
  }

  void(transactionId: string, request: VoidTransactionRequest): Observable<Transaction> {
    return this.http.post<Transaction>(`${this.base}/${transactionId}/void`, request);
  }

  bulkVoid(request: BulkVoidRequest): Observable<BulkVoidResult> {
    return this.http.post<BulkVoidResult>(`${this.base}/bulk-void`, request);
  }

  getPendingCount(scope: ProcessPendingScope, scopeId?: string | null, periodStart?: string | null, periodEnd?: string | null): Observable<{ count: number }> {
    const params: Record<string, string> = { scope };
    if (scopeId) params['scopeId'] = scopeId;
    if (periodStart) params['periodStart'] = periodStart;
    if (periodEnd) params['periodEnd'] = periodEnd;
    return this.http.get<{ count: number }>(`${this.base}/pending-count`, { params });
  }

  getEligiblePending(scope: ProcessPendingScope, scopeId?: string | null, periodStart?: string | null, periodEnd?: string | null): Observable<EligiblePendingResult> {
    const params: Record<string, string> = { scope };
    if (scopeId) params['scopeId'] = scopeId;
    if (periodStart) params['periodStart'] = periodStart;
    if (periodEnd) params['periodEnd'] = periodEnd;
    return this.http.get<EligiblePendingResult>(`${this.base}/eligible-pending`, { params });
  }

  processPending(request: ProcessPendingRequest): Observable<ProcessPendingResponse> {
    return this.http.post<ProcessPendingResponse>(`${this.base}/process-pending`, request);
  }

  getJobStatus(jobId: string): Observable<JobStatus> {
    return this.http.get<JobStatus>(`/api/jobs/${jobId}`);
  }

  cancelJob(jobId: string): Observable<void> {
    return this.http.post<void>(`/api/jobs/${jobId}/cancel`, {});
  }

  exportToExcel(filter: Record<string, unknown>): Observable<Blob> {
    return this.http.post(`${this.base}/export`, filter, { responseType: 'blob' });
  }

  getExportCount(filter: PaginationParams): Observable<{ count: number }> {
    // Re-uses the list endpoint with page=1/pageSize=1 to get totalCount without fetching data.
    // The totalCount in the response is the filtered count.
    return this.http.get<{ totalCount: number; unfilteredTotal: number | null }>(
      this.base, { params: buildHttpParams({ ...filter, page: 1, pageSize: 1 }) }
    ).pipe(map(r => ({ count: r.totalCount })));
  }
}
