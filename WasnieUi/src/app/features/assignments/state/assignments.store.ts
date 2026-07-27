import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom, Subject } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { toSignal } from '@angular/core/rxjs-interop';
import { AssignmentsApiService } from '../services/assignments.api.service';
import {
  Assignment,
  AssignmentStatus,
  AssignmentListParams,
  BulkDeleteAssignmentsResult,
  CreateAssignmentRequest,
} from '../models/assignment.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

/** Flat filter params; `buildHttpParams` spreads these onto the query string. */
function buildFilters(
  status: AssignmentStatus | null,
  payeeId: string | null,
): Record<string, string> | undefined {
  const filters: Record<string, string> = {};
  if (status) filters['status'] = status;
  if (payeeId) filters['payeeId'] = payeeId;
  return Object.keys(filters).length > 0 ? filters : undefined;
}

@Injectable({ providedIn: 'root' })
export class AssignmentsStore {
  private readonly api = inject(AssignmentsApiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  // ── Bulk selection ──────────────────────────────────────────────────────────
  readonly selectedIds = signal<Set<string>>(new Set());
  readonly selectedCount = computed(() => this.selectedIds().size);
  readonly allSelected = computed(() => {
    const items = this.assignments();
    return items.length > 0 && items.every(a => this.selectedIds().has(a.id));
  });
  readonly someSelected = computed(() => this.selectedIds().size > 0 && !this.allSelected());

  toggleSelect(id: string): void {
    this.selectedIds.update(prev => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  toggleSelectAll(): void {
    const items = this.assignments();
    if (this.allSelected()) {
      this.selectedIds.set(new Set());
    } else {
      this.selectedIds.set(new Set(items.map(a => a.id)));
    }
  }

  clearSelection(): void {
    this.selectedIds.set(new Set());
  }

  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly sortBy = signal('effectivestart');
  readonly sortOrder = signal<'asc' | 'desc'>('desc');
  readonly status = signal<AssignmentStatus | null>(null);
  /**
   * Exact payee filter, set by the "View all" deep-link from a payee's Assignments card. Kept separate
   * from `search` on purpose: search is a substring match on name/code, so it could pull in a similar
   * code, and it would also fight the user's own typing in the search box.
   */
  readonly payeeId = signal<string | null>(null);

  private readonly searchSubject$ = new Subject<string>();
  readonly search = toSignal(
    this.searchSubject$.pipe(debounceTime(300)),
    { initialValue: '' }
  );

  private _rawSearch = '';

  readonly pagedResult = signal<PagedResult<Assignment> | null>(null);

  readonly assignments = computed(() => this.pagedResult()?.items ?? []);
  readonly totalCount = computed(() => this.pagedResult()?.totalCount ?? 0);
  readonly totalPages = computed(() => this.pagedResult()?.totalPages ?? 1);

  // Legacy compat
  readonly listParams = computed<AssignmentListParams>(() => ({
    page: this.page(),
    pageSize: this.pageSize(),
    search: this._rawSearch,
    status: this.status(),
  }));

  readonly pagedAssignments = computed(() => this.assignments());
  readonly filteredAssignments = computed(() => this.assignments());

  constructor() {
    effect(() => {
      const p = this.page();
      const ps = this.pageSize();
      const sb = this.sortBy();
      const so = this.sortOrder();
      const st = this.status();
      const srch = this.search();
      const pid = this.payeeId();
      void this._loadInternal(p, ps, sb, so, st, srch, pid);
    });
  }

  private async _loadInternal(
    page: number,
    pageSize: number,
    sortBy: string,
    sortOrder: 'asc' | 'desc',
    status: AssignmentStatus | null,
    search: string,
    payeeId: string | null,
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
        filters: buildFilters(status, payeeId),
      };
      const data = await firstValueFrom(this.api.getAssignments(params));
      this.pagedResult.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  /** RefreshableStore — reload the current page/filter on route re-entry. */
  refresh(): Promise<void> {
    return this.loadAssignments();
  }

  async loadAssignments(): Promise<void> {
    await this._loadInternal(
      this.page(), this.pageSize(), this.sortBy(), this.sortOrder(),
      this.status(), this.search(), this.payeeId()
    );
  }

  /**
   * Seeds filters from the URL on entry (deep-link from a payee's Assignments card). Sets the payee
   * filter directly rather than through the debounced search path, so the very first request is
   * already filtered — otherwise the user sees the full list flash before it narrows.
   */
  loadFromQueryParams(qp: Record<string, string>): void {
    if (qp['payeeId']) this.payeeId.set(qp['payeeId']);
    if (qp['status']) this.status.set(qp['status'] as AssignmentStatus);
    this.page.set(1);
  }

  clearPayeeFilter(): void {
    this.payeeId.set(null);
    this.page.set(1);
  }

  async createAssignment(request: CreateAssignmentRequest): Promise<Assignment> {
    const assignment = await firstValueFrom(this.api.createAssignment(request));
    await this.loadAssignments();
    return assignment;
  }

  async deactivateAssignment(assignmentId: string): Promise<void> {
    await firstValueFrom(this.api.deactivateAssignment(assignmentId));
    this.pagedResult.update((r) => r
      ? { ...r, items: r.items.map((a) => a.id === assignmentId ? { ...a, status: 'Deactivated' as AssignmentStatus } : a) }
      : r);
  }

  async activateAssignment(assignmentId: string): Promise<void> {
    await firstValueFrom(this.api.activateAssignment(assignmentId));
    this.pagedResult.update((r) => r
      ? { ...r, items: r.items.map((a) => a.id === assignmentId ? { ...a, status: 'Active' as AssignmentStatus } : a) }
      : r);
  }

  async bulkActivate(ids: string[]): Promise<void> {
    await firstValueFrom(this.api.bulkActivate({ assignmentIds: ids }));
    this.pagedResult.update((r) => r
      ? { ...r, items: r.items.map((a) => ids.includes(a.id) ? { ...a, status: 'Active' as AssignmentStatus } : a) }
      : r);
    this.clearSelection();
  }

  async bulkDeactivate(ids: string[]): Promise<void> {
    await firstValueFrom(this.api.bulkDeactivate({ assignmentIds: ids }));
    this.pagedResult.update((r) => r
      ? { ...r, items: r.items.map((a) => ids.includes(a.id) ? { ...a, status: 'Deactivated' as AssignmentStatus } : a) }
      : r);
    this.clearSelection();
  }

  async bulkDelete(ids: string[]): Promise<BulkDeleteAssignmentsResult> {
    const result = await firstValueFrom(this.api.bulkDelete({ assignmentIds: ids }));
    if (result.allDeleted) {
      await this.loadAssignments();
      this.clearSelection();
    }
    return result;
  }

  setSearch(value: string): void {
    this._rawSearch = value;
    this.page.set(1);
    this.searchSubject$.next(value);
  }

  setStatus(value: AssignmentStatus | null): void {
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

  updateParams(partial: Partial<AssignmentListParams>): void {
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
}
