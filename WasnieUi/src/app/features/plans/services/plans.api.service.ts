import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Plan, PlanSummary, CreatePlanRequest } from '../models/plan.model';
import { AddRuleRequest, Rule, UpdateRuleRequest } from '../models/rule.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

@Injectable({ providedIn: 'root' })
export class PlansApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/plans';

  getPlans(params?: PaginationParams): Observable<PagedResult<PlanSummary>> {
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
    return this.http.get<PagedResult<PlanSummary>>(this.base, { params: httpParams });
  }

  getPlan(planId: string): Observable<Plan> {
    return this.http.get<Plan>(`${this.base}/${planId}`);
  }

  getPlanVersions(planName: string, params?: PaginationParams): Observable<PagedResult<PlanSummary>> {
    let httpParams = new HttpParams();
    if (params) {
      httpParams = httpParams.set('page', String(params.page));
      httpParams = httpParams.set('pageSize', String(params.pageSize));
      if (params.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
      if (params.sortOrder) httpParams = httpParams.set('sortOrder', params.sortOrder);
    }
    return this.http.get<PagedResult<PlanSummary>>(`${this.base}/versions/${encodeURIComponent(planName)}`, { params: httpParams });
  }

  createPlan(request: CreatePlanRequest): Observable<Plan> {
    return this.http.post<Plan>(this.base, request);
  }

  clonePlan(planId: string): Observable<Plan> {
    return this.http.post<Plan>(`${this.base}/${planId}/clone`, {});
  }

  activatePlan(planId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${planId}/activate`, {});
  }

  archivePlan(planId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${planId}/archive`, {});
  }

  addRule(planId: string, request: AddRuleRequest): Observable<Rule> {
    return this.http.post<Rule>(`${this.base}/${planId}/rules`, request);
  }

  updateRule(planId: string, ruleId: string, request: UpdateRuleRequest): Observable<Rule> {
    return this.http.put<Rule>(`${this.base}/${planId}/rules/${ruleId}`, request);
  }

  deletePlan(planId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${planId}`);
  }

  deleteRule(planId: string, ruleId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${planId}/rules/${ruleId}`);
  }

  getPlanAssignments(planId: string, params?: PaginationParams): Observable<PagedResult<import('../../assignments/models/assignment.model').Assignment>> {
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
      `/api/assignments/plan/${planId}`, { params: httpParams });
  }
}
