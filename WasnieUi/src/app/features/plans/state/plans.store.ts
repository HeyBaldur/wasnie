import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom, Subject } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { toSignal } from '@angular/core/rxjs-interop';
import { PlansApiService } from '../services/plans.api.service';
import { Plan, PlanSummary, PlanStatus, CreatePlanRequest } from '../models/plan.model';
import { PlanListParams } from '../models/plan-list.params';
import { AddRuleRequest, Rule, UpdateRuleRequest } from '../models/rule.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

@Injectable({ providedIn: 'root' })
export class PlansStore {
  private readonly api = inject(PlansApiService);

  readonly selectedPlan = signal<Plan | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly sortBy = signal('name');
  readonly sortOrder = signal<'asc' | 'desc'>('asc');
  readonly status = signal<PlanStatus | null>(null);

  private readonly searchSubject$ = new Subject<string>();
  readonly search = toSignal(
    this.searchSubject$.pipe(debounceTime(300)),
    { initialValue: '' }
  );

  private _rawSearch = '';

  readonly pagedResult = signal<PagedResult<PlanSummary> | null>(null);

  readonly plans = computed(() => this.pagedResult()?.items ?? []);
  readonly totalCount = computed(() => this.pagedResult()?.totalCount ?? 0);
  readonly totalPages = computed(() => this.pagedResult()?.totalPages ?? 1);
  readonly unfilteredTotal = signal<number>(0);

  // Legacy compat
  readonly listParams = computed<PlanListParams>(() => ({
    page: this.page(),
    pageSize: this.pageSize(),
    search: this._rawSearch,
    status: this.status(),
  }));

  readonly pagedPlans = computed(() => this.plans());
  readonly filteredPlans = computed(() => this.plans());

  // versions are still loaded all at once (small list)
  readonly versions = signal<PlanSummary[]>([]);

  constructor() {
    effect(() => {
      const p = this.page();
      const ps = this.pageSize();
      const sb = this.sortBy();
      const so = this.sortOrder();
      const st = this.status();
      const srch = this.search();
      void this._loadInternal(p, ps, sb, so, st, srch);
    });
  }

  private async _loadInternal(
    page: number,
    pageSize: number,
    sortBy: string,
    sortOrder: 'asc' | 'desc',
    status: PlanStatus | null,
    search: string,
  ): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const params: PaginationParams = {
        page,
        pageSize,
        sortBy,
        sortOrder,
        search: search || undefined,
        filters: status ? { status } : undefined,
      };
      const data = await firstValueFrom(this.api.getPlans(params));
      this.pagedResult.set(data);
      if (!search && !status) {
        this.unfilteredTotal.set(data.totalCount);
      }
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  /** RefreshableStore — reload the current page/filter on route re-entry. */
  refresh(): Promise<void> {
    return this.loadPlans();
  }

  async loadPlans(): Promise<void> {
    await this._loadInternal(
      this.page(), this.pageSize(), this.sortBy(), this.sortOrder(),
      this.status(), this.search()
    );
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
      const data = await firstValueFrom(this.api.getPlanVersions(planName, { page: 1, pageSize: 100, sortBy: 'version', sortOrder: 'desc' }));
      this.versions.set(data.items);
    } catch {
      this.versions.set([]);
    }
  }

  async createPlan(request: CreatePlanRequest): Promise<Plan> {
    const plan = await firstValueFrom(this.api.createPlan(request));
    await this.loadPlans();
    return plan;
  }

  async deletePlan(planId: string): Promise<void> {
    await firstValueFrom(this.api.deletePlan(planId));
    await this.loadPlans();
  }

  async clonePlan(planId: string): Promise<Plan> {
    const plan = await firstValueFrom(this.api.clonePlan(planId));
    await this.loadPlans();
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

  setSearch(value: string): void {
    this._rawSearch = value;
    this.page.set(1);
    this.searchSubject$.next(value);
  }

  setStatus(value: PlanStatus | null): void {
    this.status.set(value);
    this.page.set(1);
  }

  setPage(value: number): void {
    this.page.set(value);
  }

  setPageSize(value: number): void {
    this.pageSize.set(value);
    this.page.set(1);
  }

  updateParams(partial: Partial<PlanListParams>): void {
    if ('search' in partial && partial.search !== undefined) {
      this.setSearch(partial.search);
    }
    if ('status' in partial) {
      this.status.set(partial.status ?? null);
    }
    if ('pageSize' in partial && partial.pageSize !== undefined) {
      this.setPageSize(partial.pageSize);
    }
    if ('page' in partial && partial.page !== undefined) {
      this.page.set(partial.page);
    } else if ('search' in partial || 'status' in partial || 'pageSize' in partial) {
      this.page.set(1);
    }
  }

  private _patchStatus(planId: string, status: PlanStatus): void {
    this.pagedResult.update((r) => r
      ? { ...r, items: r.items.map((p) => p.id === planId ? { ...p, status } : p) }
      : r);
    this.selectedPlan.update((p) => p?.id === planId ? { ...p, status } : p);
  }
}
