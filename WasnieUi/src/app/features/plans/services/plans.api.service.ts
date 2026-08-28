import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Plan, PlanSummary, CreatePlanRequest } from '../models/plan.model';
import {
  AddRuleRequest, Rule, RuleSimulation, SimulateRuleRequest, TriggerField, UpdateRuleRequest,
} from '../models/rule.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';
import { buildHttpParams } from '../../../shared/utils/build-http-params';

export interface OtherActivePlan {
  planId: string;
  planName: string;
}

export interface MultiPlanPayee {
  payeeId: string;
  fullName: string;
  employeeCode: string;
  otherPlans: OtherActivePlan[];
}

export interface MultiPlanPayees {
  count: number;
  items: MultiPlanPayee[];
}

@Injectable({ providedIn: 'root' })
export class PlansApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/plans';

  getPlans(params?: PaginationParams): Observable<PagedResult<PlanSummary>> {
    return this.http.get<PagedResult<PlanSummary>>(this.base, { params: buildHttpParams(params) });
  }

  getPlan(planId: string): Observable<Plan> {
    return this.http.get<Plan>(`${this.base}/${planId}`);
  }

  getPlanVersions(planName: string, params?: PaginationParams): Observable<PagedResult<PlanSummary>> {
    return this.http.get<PagedResult<PlanSummary>>(
      `${this.base}/versions/${encodeURIComponent(planName)}`, { params: buildHttpParams(params) });
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

  /**
   * Turns the clawback on (or off, with both nulls) for this plan. Nothing here computes money —
   * it stores the policy the engine reads when a pay run is settled.
   */
  setClawbackPolicy(
    planId: string,
    policy: { maturationDays: number | null; capPercent: number | null },
  ): Observable<void> {
    return this.http.put<void>(`${this.base}/${planId}/clawback-policy`, policy);
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

  /**
   * What one hypothetical transaction would earn under a rule, step by step.
   *
   * ★ POST, and nothing is created. The rule's whole definition travels in the body because that is
   * an object, not a query string — the server writes nothing: no credit, no ledger entry, no counter.
   */
  simulateRule(planId: string, request: SimulateRuleRequest): Observable<RuleSimulation> {
    return this.http.post<RuleSimulation>(`${this.base}/${planId}/rules/simulate`, request);
  }

  deletePlan(planId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${planId}`);
  }

  deleteRule(planId: string, ruleId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${planId}/rules/${ruleId}`);
  }

  getPlanAssignments(planId: string, params?: PaginationParams): Observable<PagedResult<import('../../assignments/models/assignment.model').Assignment>> {
    return this.http.get<PagedResult<import('../../assignments/models/assignment.model').Assignment>>(
      `/api/assignments/plan/${planId}`, { params: buildHttpParams(params) });
  }

  /** The engine's own trigger-field catalog — the rule builder must not keep its own copy. */
  getTriggerFields(): Observable<TriggerField[]> {
    return this.http.get<TriggerField[]>(`${this.base}/trigger-fields`);
  }

  /** Distinct category values that exist for the tenant — the choices for a condition on `category`. */
  getCategoryValues(): Observable<string[]> {
    return this.http.get<string[]>(`${this.base}/category-values`);
  }

  getMultiPlanPayees(planId: string): Observable<MultiPlanPayees> {
    return this.http.get<MultiPlanPayees>(`${this.base}/${planId}/multi-plan-payees`);
  }
}
