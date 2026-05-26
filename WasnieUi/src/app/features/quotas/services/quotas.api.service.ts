import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { QuotaSummary, CreateQuotaRequest, UpdateQuotaRequest } from '../models/quota.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

@Injectable({ providedIn: 'root' })
export class QuotasApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/quotas';

  getQuotas(params?: PaginationParams): Observable<PagedResult<QuotaSummary>> {
    let httpParams = new HttpParams();
    if (params) {
      httpParams = httpParams.set('page', String(params.page));
      httpParams = httpParams.set('pageSize', String(params.pageSize));
      if (params.search) httpParams = httpParams.set('search', params.search);
      if (params.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
      if (params.sortOrder) httpParams = httpParams.set('sortOrder', params.sortOrder);
      if (params.filters) {
        Object.entries(params.filters).forEach(([key, value]) => {
          httpParams = httpParams.set(`filters[${key}]`, value);
        });
      }
    }
    return this.http.get<PagedResult<QuotaSummary>>(this.base, { params: httpParams });
  }

  getQuota(quotaId: string): Observable<QuotaSummary> {
    return this.http.get<QuotaSummary>(`${this.base}/${quotaId}`);
  }

  createQuota(request: CreateQuotaRequest): Observable<QuotaSummary> {
    return this.http.post<QuotaSummary>(this.base, request);
  }

  updateQuota(quotaId: string, request: UpdateQuotaRequest): Observable<QuotaSummary> {
    return this.http.put<QuotaSummary>(`${this.base}/${quotaId}`, { ...request, quotaId });
  }

  activateQuota(quotaId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${quotaId}/activate`, {});
  }

  closeQuota(quotaId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${quotaId}/close`, {});
  }

  listByPayee(payeeId: string, params?: PaginationParams): Observable<PagedResult<QuotaSummary>> {
    let httpParams = new HttpParams();
    if (params) {
      httpParams = httpParams.set('page', String(params.page));
      httpParams = httpParams.set('pageSize', String(params.pageSize));
      if (params.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
      if (params.sortOrder) httpParams = httpParams.set('sortOrder', params.sortOrder);
      if (params.filters) {
        Object.entries(params.filters).forEach(([key, value]) => {
          httpParams = httpParams.set(`filters[${key}]`, value);
        });
      }
    }
    return this.http.get<PagedResult<QuotaSummary>>(`${this.base}/payee/${payeeId}`, { params: httpParams });
  }
}
