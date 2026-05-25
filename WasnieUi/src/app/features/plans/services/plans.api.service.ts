import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Plan, PlanSummary, PlanVersion, CreatePlanRequest } from '../models/plan.model';
import { PlanStatus } from '../models/plan.model';
import { AddRuleRequest, Rule, UpdateRuleRequest } from '../models/rule.model';

@Injectable({ providedIn: 'root' })
export class PlansApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/plans';

  getPlans(status?: PlanStatus): Observable<PlanSummary[]> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    return this.http.get<PlanSummary[]>(this.base, { params });
  }

  getPlan(planId: string): Observable<Plan> {
    return this.http.get<Plan>(`${this.base}/${planId}`);
  }

  getPlanVersions(planName: string): Observable<PlanVersion[]> {
    return this.http.get<PlanVersion[]>(`${this.base}/versions/${encodeURIComponent(planName)}`);
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
}
