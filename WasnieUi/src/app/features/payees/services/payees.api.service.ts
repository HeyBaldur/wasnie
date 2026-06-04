import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Payee, CreatePayeeRequest, UpdatePayeeRequest } from '../models/payee.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';
import { buildHttpParams } from '../../../shared/utils/build-http-params';

@Injectable({ providedIn: 'root' })
export class PayeesApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/payees';

  getPayees(params?: PaginationParams): Observable<PagedResult<Payee>> {
    return this.http.get<PagedResult<Payee>>(this.base, { params: buildHttpParams(params) });
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

  deactivate(payeeId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${payeeId}/deactivate`, {});
  }

  activate(payeeId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${payeeId}/activate`, {});
  }

  getPayeeAssignments(payeeId: string, params?: PaginationParams): Observable<PagedResult<import('../../assignments/models/assignment.model').Assignment>> {
    return this.http.get<PagedResult<import('../../assignments/models/assignment.model').Assignment>>(
      `${this.base}/${payeeId}/assignments`, { params: buildHttpParams(params) });
  }

  getPayeeQuotas(payeeId: string, params?: PaginationParams): Observable<PagedResult<import('../../quotas/models/quota.model').QuotaSummary>> {
    return this.http.get<PagedResult<import('../../quotas/models/quota.model').QuotaSummary>>(
      `${this.base}/${payeeId}/quotas`, { params: buildHttpParams(params) });
  }

  getPayeeAttainment(payeeId: string): Observable<import('../../quotas/models/quota.model').QuotaAttainment[]> {
    return this.http.get<import('../../quotas/models/quota.model').QuotaAttainment[]>(
      `${this.base}/${payeeId}/attainment`);
  }

  getPayeeDashboard(payeeId: string, period = 'active'): Observable<import('../models/payee-dashboard.model').PayeeDashboard> {
    return this.http.get<import('../models/payee-dashboard.model').PayeeDashboard>(
      `${this.base}/${payeeId}/dashboard`, { params: { period } });
  }

  getPayeeCredits(payeeId: string, page: number, period = 'active'): Observable<import('../../../shared/models/pagination.models').PagedResult<import('../../credits/models/credit.model').CreditListItem>> {
    return this.http.get<import('../../../shared/models/pagination.models').PagedResult<import('../../credits/models/credit.model').CreditListItem>>(
      `${this.base}/${payeeId}/credits`,
      { params: { page: String(page), pageSize: '10', period } });
  }
}
