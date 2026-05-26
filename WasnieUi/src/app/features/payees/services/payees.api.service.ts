import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Payee, CreatePayeeRequest, UpdatePayeeRequest } from '../models/payee.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

@Injectable({ providedIn: 'root' })
export class PayeesApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/payees';

  getPayees(params?: PaginationParams): Observable<PagedResult<Payee>> {
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
    return this.http.get<PagedResult<Payee>>(this.base, { params: httpParams });
  }

  getPayee(payeeId: string): Observable<Payee> {
    return this.http.get<Payee>(`${this.base}/${payeeId}`);
  }

  createPayee(request: CreatePayeeRequest): Observable<Payee> {
    return this.http.post<Payee>(this.base, request);
  }

  updatePayee(payeeId: string, request: UpdatePayeeRequest): Observable<Payee> {
    return this.http.put<Payee>(`${this.base}/${payeeId}`, { ...request, payeeId });
  }

  markAsActive(payeeId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${payeeId}/mark-active`, {});
  }

  markAsOnLeave(payeeId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${payeeId}/mark-on-leave`, {});
  }

  markAsTerminated(payeeId: string, terminationDate: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${payeeId}/mark-terminated`, { terminationDate });
  }

  getPayeeAssignments(payeeId: string, params?: PaginationParams): Observable<PagedResult<import('../../assignments/models/assignment.model').Assignment>> {
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
    return this.http.get<PagedResult<import('../../assignments/models/assignment.model').Assignment>>(
      `${this.base}/${payeeId}/assignments`, { params: httpParams });
  }

  getPayeeQuotas(payeeId: string, params?: PaginationParams): Observable<PagedResult<import('../../quotas/models/quota.model').QuotaSummary>> {
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
    return this.http.get<PagedResult<import('../../quotas/models/quota.model').QuotaSummary>>(
      `${this.base}/${payeeId}/quotas`, { params: httpParams });
  }
}
