import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PlansApiService } from '../services/plans.api.service';
import { Plan, PlanSummary, PlanVersion, PlanStatus, CreatePlanRequest } from '../models/plan.model';
import { PlanListParams } from '../models/plan-list.params';
import { AddRuleRequest, Rule, UpdateRuleRequest } from '../models/rule.model';

@Injectable({ providedIn: 'root' })
export class PlansStore {
  private readonly api = inject(PlansApiService);

  readonly plans = signal<PlanSummary[]>([]);
  readonly selectedPlan = signal<Plan | null>(null);
  readonly versions = signal<PlanVersion[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly listParams = signal<PlanListParams>({
    page: 1,
    pageSize: 15,
    status: null,
    search: '',
  });

  readonly filteredPlans = computed(() => {
    const { status, search } = this.listParams();
    let result = this.plans();
    if (status) result = result.filter((p) => p.status === status);
    if (search.trim()) {
      const q = search.trim().toLowerCase();
      result = result.filter((p) => p.name.toLowerCase().includes(q));
    }
    return result;
  });

  readonly totalCount = computed(() => this.filteredPlans().length);

  readonly pagedPlans = computed(() => {
    const { page, pageSize } = this.listParams();
    const start = (page - 1) * pageSize;
    return this.filteredPlans().slice(start, start + pageSize);
  });

  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.listParams().pageSize))
  );

  async loadPlans(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const data = await firstValueFrom(this.api.getPlans());
      this.plans.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  async loadPlan(planId: string): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const data = await firstValueFrom(this.api.getPlan(planId));
      this.selectedPlan.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  async loadVersions(planName: string): Promise<void> {
    try {
      const data = await firstValueFrom(this.api.getPlanVersions(planName));
      this.versions.set(data);
    } catch {
      this.versions.set([]);
    }
  }

  async createPlan(request: CreatePlanRequest): Promise<Plan> {
    const plan = await firstValueFrom(this.api.createPlan(request));
    this.plans.update((list) => [
      { ...plan, activeRuleCount: 0 },
      ...list,
    ]);
    return plan;
  }

  async deletePlan(planId: string): Promise<void> {
    await firstValueFrom(this.api.deletePlan(planId));
    this.plans.update((list) => list.filter((p) => p.id !== planId));
  }

  async clonePlan(planId: string): Promise<Plan> {
    const plan = await firstValueFrom(this.api.clonePlan(planId));
    this.plans.update((list) => [{ ...plan, activeRuleCount: 0 }, ...list]);
    return plan;
  }

  async activatePlan(planId: string): Promise<void> {
    await firstValueFrom(this.api.activatePlan(planId));
    this._patchStatus(planId, 'Active');
  }

  async archivePlan(planId: string): Promise<void> {
    await firstValueFrom(this.api.archivePlan(planId));
    this._patchStatus(planId, 'Archived');
  }

  async addRule(planId: string, request: AddRuleRequest): Promise<Rule> {
    const rule = await firstValueFrom(this.api.addRule(planId, request));
    this.selectedPlan.update((p) =>
      p ? { ...p, rules: [...p.rules, rule] } : p
    );
    return rule;
  }

  async updateRule(planId: string, ruleId: string, request: UpdateRuleRequest): Promise<Rule> {
    const rule = await firstValueFrom(this.api.updateRule(planId, ruleId, request));
    this.selectedPlan.update((p) =>
      p ? { ...p, rules: p.rules.map((r) => (r.id === ruleId ? rule : r)) } : p
    );
    return rule;
  }

  async deleteRule(planId: string, ruleId: string): Promise<void> {
    await firstValueFrom(this.api.deleteRule(planId, ruleId));
    this.selectedPlan.update((p) =>
      p ? { ...p, rules: p.rules.filter((r) => r.id !== ruleId) } : p
    );
  }

  updateParams(partial: Partial<PlanListParams>): void {
    this.listParams.update((p) => ({ ...p, ...partial, page: 'page' in partial ? (partial.page ?? 1) : 1 }));
  }

  private _patchStatus(planId: string, status: PlanStatus): void {
    this.plans.update(list => list.map(p => p.id === planId ? { ...p, status } : p));
    this.selectedPlan.update(p => p?.id === planId ? { ...p, status } : p);
  }
}
