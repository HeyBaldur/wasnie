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

  getById(id: string, page = 1, pageSize = 25, excludeZero = false): Observable<PayRunDetail> {
    let params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    if (excludeZero) params = params.set('excludeZero', 'true');
    return this.http.get<PayRunDetail>(`${this.base}/${id}`, { params });
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
