import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';
import { buildHttpParams } from '../../../shared/utils/build-http-params';
import { JobState } from '../../imports/transactions/models/transaction-import.models';
import {
  BulkApproveRequest,
  BulkApproveResult,
  BulkMarkPaidRequest,
  BulkMarkPaidResult,
  CalculatePayoutsRequest,
  OverlappingPayout,
  PayoutDetail,
  PayoutListItem,
  DiscardPayoutResult,
} from '../models/payout.model';

export interface PayoutJobStatus {
  id: string;
  state: JobState;
  errorMessage: string | null;
  resultSummary: string | null;
}

@Injectable({ providedIn: 'root' })
export class PayoutsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/payouts';

  list(params?: PaginationParams): Observable<PagedResult<PayoutListItem>> {
    return this.http.get<PagedResult<PayoutListItem>>(this.base, {
      params: buildHttpParams(params),
    });
  }

  getById(id: string): Observable<PayoutDetail> {
    return this.http.get<PayoutDetail>(`${this.base}/${id}`);
  }

  calculate(body: CalculatePayoutsRequest): Observable<{ jobId: string }> {
    return this.http.post<{ jobId: string }>(`${this.base}/calculate`, body);
  }

  approve(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/approve`, {});
  }

  /**
   * Close an Approved payout that can never be paid (KAN-52).
   *
   * ★ ONLY THE REASON TRAVELS. Whether the payout is genuinely unpayable is the server's call,
   * against the credits — a client that could assert it could retire a debt somebody is still owed.
   */
  discard(id: string, reason: string): Observable<DiscardPayoutResult> {
    return this.http.post<DiscardPayoutResult>(`${this.base}/${id}/discard`, { reason });
  }

  markPaid(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/mark-paid`, {});
  }

  bulkApprove(body: BulkApproveRequest): Observable<BulkApproveResult> {
    return this.http.post<BulkApproveResult>(`${this.base}/bulk-approve`, body);
  }

  bulkMarkPaid(body: BulkMarkPaidRequest): Observable<BulkMarkPaidResult> {
    return this.http.post<BulkMarkPaidResult>(`${this.base}/bulk-mark-paid`, body);
  }

  getOverlaps(id: string): Observable<OverlappingPayout[]> {
    return this.http.get<OverlappingPayout[]>(`${this.base}/${id}/overlaps`);
  }

  checkBulkOverlaps(ids: string[]): Observable<{ count: number }> {
    return this.http.post<{ count: number }>(`${this.base}/overlaps-check`, { payoutIds: ids });
  }

  getJobStatus(jobId: string): Observable<PayoutJobStatus> {
    return this.http.get<PayoutJobStatus>(`/api/jobs/${jobId}`);
  }

  exportPdf(id: string): Observable<Blob> {
    return this.http.get(`${this.base}/${id}/export/pdf`, {
      responseType: 'blob' as const,
    });
  }

  exportToExcel(filters: Record<string, string>): Observable<Blob> {
    let params = new HttpParams();
    Object.entries(filters).forEach(([k, v]) => { params = params.set(k, v); });
    return this.http.get(`${this.base}/export`, {
      params,
      responseType: 'blob' as const,
    });
  }
}
