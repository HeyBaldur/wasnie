import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';
import { buildHttpParams } from '../../../shared/utils/build-http-params';
import { CreditListItem, CreditCounters, CreditByPayee, CreditDetail } from '../models/credit.model';

@Injectable({ providedIn: 'root' })
export class CreditsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/credits';

  list(params?: PaginationParams): Observable<PagedResult<CreditListItem>> {
    return this.http.get<PagedResult<CreditListItem>>(this.base, { params: buildHttpParams(params) });
  }

  counters(params?: PaginationParams): Observable<CreditCounters> {
    return this.http.get<CreditCounters>(`${this.base}/counters`, { params: buildHttpParams(params) });
  }

  byPayee(params?: PaginationParams): Observable<CreditByPayee[]> {
    return this.http.get<CreditByPayee[]>(`${this.base}/by-payee`, { params: buildHttpParams(params) });
  }

  getById(id: string): Observable<CreditDetail> {
    return this.http.get<CreditDetail>(`${this.base}/${id}`);
  }
}
