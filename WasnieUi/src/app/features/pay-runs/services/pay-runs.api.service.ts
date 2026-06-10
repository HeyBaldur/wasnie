import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';
import { buildHttpParams } from '../../../shared/utils/build-http-params';
import {
  CalculatePayRunRequest,
  CalculatePayRunResult,
  PayRunDetail,
  PayRunListItem,
  PayRunPayoutsDetailFilter,
} from '../models/pay-run.model';

@Injectable({ providedIn: 'root' })
export class PayRunsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/pay-runs';

  list(params?: PaginationParams): Observable<PagedResult<PayRunListItem>> {
    return this.http.get<PagedResult<PayRunListItem>>(this.base, {
      params: buildHttpParams(params),
    });
  }

  getById(id: string, filter: PayRunPayoutsDetailFilter, page = 1, pageSize = 25): Observable<PayRunDetail> {
    let params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    if (filter.excludeZero) params = params.set('excludeZero', 'true');
    if (filter.status) params = params.set('status', filter.status);
    if (filter.periodFrom) params = params.set('periodFrom', filter.periodFrom);
    if (filter.periodTo) params = params.set('periodTo', filter.periodTo);
    if (filter.amountMin != null) params = params.set('amountMin', String(filter.amountMin));
    if (filter.amountMax != null) params = params.set('amountMax', String(filter.amountMax));
    if (filter.payeeIds.length > 0) params = params.set('payeeIds', filter.payeeIds.join(','));
    if (filter.planIds.length > 0) params = params.set('planIds', filter.planIds.join(','));
    if (filter.currencies.length > 0) params = params.set('currencies', filter.currencies.join(','));
    return this.http.get<PayRunDetail>(`${this.base}/${id}`, { params });
  }

  exportPayRuns(filterParams: Record<string, string>): Observable<Blob> {
    let params = new HttpParams();
    Object.entries(filterParams).forEach(([k, v]) => { params = params.set(k, v); });
    return this.http.get(`${this.base}/export`, { params, responseType: 'blob' as const });
  }

  exportRunPayouts(runId: string, filterParams: Record<string, string>): Observable<Blob> {
    let params = new HttpParams().set('payRunId', runId);
    Object.entries(filterParams).forEach(([k, v]) => { params = params.set(k, v); });
    return this.http.get('/api/payouts/export', { params, responseType: 'blob' as const });
  }

  calculate(body: CalculatePayRunRequest): Observable<CalculatePayRunResult> {
    return this.http.post<CalculatePayRunResult>(`${this.base}/calculate`, body);
  }

  approve(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/approve`, {});
  }

  markPaid(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/mark-paid`, {});
  }

  reopen(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/reopen`, {});
  }
}
