import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { QuotaSummary, CreateQuotaRequest, UpdateQuotaRequest } from '../models/quota.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';
import { buildHttpParams } from '../../../shared/utils/build-http-params';

@Injectable({ providedIn: 'root' })
export class QuotasApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/quotas';

  getQuotas(params?: PaginationParams): Observable<PagedResult<QuotaSummary>> {
    return this.http.get<PagedResult<QuotaSummary>>(this.base, { params: buildHttpParams(params) });
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
    return this.http.get<PagedResult<QuotaSummary>>(
      `${this.base}/payee/${payeeId}`, { params: buildHttpParams(params) });
  }
}
