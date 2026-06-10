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
  PayoutDetail,
  PayoutListItem,
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

  markPaid(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/mark-paid`, {});
  }

  bulkApprove(body: BulkApproveRequest): Observable<BulkApproveResult> {
    return this.http.post<BulkApproveResult>(`${this.base}/bulk-approve`, body);
  }

  bulkMarkPaid(body: BulkMarkPaidRequest): Observable<BulkMarkPaidResult> {
    return this.http.post<BulkMarkPaidResult>(`${this.base}/bulk-mark-paid`, body);
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
