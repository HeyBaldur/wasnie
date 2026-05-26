import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom, Subject } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { toSignal } from '@angular/core/rxjs-interop';
import { AssignmentsApiService } from '../services/assignments.api.service';
import { Assignment, AssignmentStatus, AssignmentListParams, CreateAssignmentRequest } from '../models/assignment.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

@Injectable({ providedIn: 'root' })
export class AssignmentsStore {
  private readonly api = inject(AssignmentsApiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly sortBy = signal('effectivestart');
  readonly sortOrder = signal<'asc' | 'desc'>('desc');
  readonly status = signal<AssignmentStatus | null>(null);

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
      void this._loadInternal(p, ps, sb, so, st, srch);
    });
  }

  private async _loadInternal(
    page: number,
    pageSize: number,
    sortBy: string,
    sortOrder: 'asc' | 'desc',
    status: AssignmentStatus | null,
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
      const data = await firstValueFrom(this.api.getAssignments(params));
      this.pagedResult.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  async loadAssignments(): Promise<void> {
    await this._loadInternal(
      this.page(), this.pageSize(), this.sortBy(), this.sortOrder(),
      this.status(), this.search()
    );
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
