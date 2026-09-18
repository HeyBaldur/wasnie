import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom, Subject } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { toSignal } from '@angular/core/rxjs-interop';
import { PlansApiService } from '../services/plans.api.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { Plan, PlanSummary, PlanStatus, CreatePlanRequest } from '../models/plan.model';
import { PlanListParams } from '../models/plan-list.params';
import { AddRuleRequest, Rule, UpdateRuleRequest } from '../models/rule.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';
import { LatestRequestGuard } from '../../../shared/state/latest-request-guard';

@Injectable({ providedIn: 'root' })
export class PlansStore {
  private readonly api = inject(PlansApiService);
  private readonly currentUser = inject(CurrentUserService);

  readonly selectedPlan = signal<Plan | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(10);
  // Default the list to creation order, newest first — the most recently created plan on top.
  readonly sortBy = signal('createdat');
  readonly sortOrder = signal<'asc' | 'desc'>('desc');
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

  // Makes the last-REQUESTED load win rather than the last-ARRIVED one. Change the filter while a
  // fetch is still in flight and two requests race; without this the slower, wider query can land
  // last and overwrite the narrower one, leaving the list looking unfiltered until a manual reload.
  private readonly _latest = new LatestRequestGuard();

  /**
   * The detail page's OWN state, separate from the list's.
   *
   * ★★ ONE FIELD, ONE MEANING (§B3) — AND THIS IS THE BUG THAT BOUGHT IT. `loading` and `error` were
   * shared by two unrelated requests: "the catalogue" and "this one plan". The detail template reads
   * `error` BEFORE `selectedPlan`, so a failure of the LIST blanked a plan that had loaded perfectly,
   * with "Something went wrong. Please try again." over a page that had the data in hand.
   *
   * ★★ IT SURFACED AS A RACE, WHICH IS WHY IT LOOKED INTERMITTENT. Opening a plan fires both requests
   * at once; each clears `error` when it starts and writes it when it fails, so whichever finished
   * LAST decided the screen. Arriving by click, few requests are in flight and the plan usually won.
   * On a refresh the list queues behind a dozen bootstrap calls, lands last, and the page broke every
   * time — the exact "works on click, fails on refresh" the report describes.
   *
   * ★ IT IS NOT A REP-ONLY BUG. A rep makes the list fail every time (they hold no `Plans.Read`, by
   * design), so they meet it constantly; for anybody else a slow or failed catalogue does the same
   * thing. Separating the state fixes it for every reader instead of special-casing a role.
   */
  readonly planLoading = signal(false);
  readonly planError = signal<string | null>(null);

  constructor() {
    effect(() => {
      const p = this.page();
      const ps = this.pageSize();
      const sb = this.sortBy();
      const so = this.sortOrder();
      const st = this.status();
      const srch = this.search();

      // ★★ THE CATALOGUE IS NOT FETCHED FOR SOMEBODY WHO MAY NOT READ IT. This effect runs the moment
      // the store is first injected — including from the DETAIL page, which needs one plan and not the
      // list. For a rep holding only `Plans.ReadOwn` that request is a guaranteed 403 and, because
      // ListPlansHandler refuses through `RequireAsync`, a PermissionDenied audit row every single
      // time they open their own plan. Asking first costs nothing and keeps the trail meaningful.
      if (!this.currentUser.hasPermission('Plans.Read')) return;

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
    const token = this._latest.begin();
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
      if (this._latest.isStale(token)) return;   // superseded by a newer load — discard
      this.pagedResult.set(data);
      if (!search && !status) {
        this.unfilteredTotal.set(data.totalCount);
      }
    } catch {
      if (this._latest.isStale(token)) return;   // don't let a stale failure clobber a fresh result
      this.error.set('ERRORS.GENERIC');
    } finally {
      // Only the newest request owns the spinner; a stale one finishing must not clear it.
      if (!this._latest.isStale(token)) this.loading.set(false);
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

  /** Loads ONE plan. Writes only the detail's state — never the list's. */
  async loadPlan(planId: string): Promise<void> {
    this.planLoading.set(true);
    this.planError.set(null);
    try {
      const data = await firstValueFrom(this.api.getPlan(planId));
      this.selectedPlan.set(data);
    } catch {
      // ★ AND IT CLEARS THE PLAN. A refused or missing plan must not leave the previous one on screen
      // under an error message — that is two contradictory things said at once.
      this.selectedPlan.set(null);
      this.planError.set('ERRORS.GENERIC');
    } finally {
      this.planLoading.set(false);
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
