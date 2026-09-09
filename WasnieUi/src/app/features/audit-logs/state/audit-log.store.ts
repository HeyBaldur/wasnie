import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LatestRequestGuard } from '../../../shared/state/latest-request-guard';
import { AuditLogApiService } from '../services/audit-log.api.service';
import {
  AuditLogDetail,
  AuditLogFilter,
  AuditLogPage,
  AuditLogRow,
  EMPTY_AUDIT_LOG_FILTER,
} from '../models/audit-log.model';

/**
 * The audit trail's state (KAN-19).
 *
 * ★ MIRRORS THE RECONCILIATION STORE DELIBERATELY (§5.1) — same signals, same load/goToPage/
 * setPageSize shape, same "reset to page 1 when the page size changes" rule. A reader who has used
 * one list in this app has used them all, and a fifth pagination behaviour is a bug waiting for a
 * reporter.
 */
@Injectable({ providedIn: 'root' })
export class AuditLogStore {
  private readonly api = inject(AuditLogApiService);

  private readonly _rows = signal<readonly AuditLogRow[]>([]);
  private readonly _total = signal(0);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _filter = signal<AuditLogFilter>(EMPTY_AUDIT_LOG_FILTER);
  private readonly _actions = signal<readonly string[]>([]);
  private readonly _actors = signal<readonly string[]>([]);

  readonly rows = this._rows.asReadonly();
  readonly total = this._total.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly filter = this._filter.asReadonly();
  readonly actions = this._actions.asReadonly();
  readonly actors = this._actors.asReadonly();

  readonly totalPages = computed(() => {
    const size = this._filter().pageSize;
    return size > 0 ? Math.max(1, Math.ceil(this._total() / size)) : 1;
  });

  readonly isEmpty = computed(() => !this._loading() && this._rows().length === 0);

  readonly activeFilterCount = computed(() => {
    const f = this._filter();
    return [f.action, f.actor, f.from, f.to].filter((v) => v !== null).length;
  });

  readonly hasActiveFilters = computed(() => this.activeFilterCount() > 0);

  // ── The detail drawer ───────────────────────────────────────────────────────

  private readonly _detail = signal<AuditLogDetail | null>(null);
  private readonly _detailLoading = signal(false);
  private readonly _detailError = signal<string | null>(null);

  readonly detail = this._detail.asReadonly();
  readonly detailLoading = this._detailLoading.asReadonly();
  readonly detailError = this._detailError.asReadonly();

  /**
   * Makes the last-REQUESTED response win rather than the last-ARRIVED one. Without it, two loads
   * racing (a filter changed while a fetch was in flight) leave whichever the network returns last on
   * screen — typically the older, wider query, so the user stares at unfiltered rows under a filtered
   * UI until they press reload.
   */
  private readonly _latest = new LatestRequestGuard();

  async load(filter?: Partial<AuditLogFilter>): Promise<void> {
    const next = { ...this._filter(), ...filter };
    this._filter.set(next);
    const token = this._latest.begin();
    this._loading.set(true);
    this._error.set(null);

    try {
      const page: AuditLogPage = await firstValueFrom(this.api.list(next));
      if (this._latest.isStale(token)) return;   // superseded by a newer load — discard
      this._rows.set(page.items);
      this._total.set(page.totalCount);
    } catch {
      if (this._latest.isStale(token)) return;   // don't let a stale failure clobber a fresh result
      this._error.set('AUDIT.LOAD_ERROR');
      this._rows.set([]);
      this._total.set(0);
    } finally {
      if (!this._latest.isStale(token)) this._loading.set(false);
    }
  }

  async loadOptions(): Promise<void> {
    try {
      const options = await firstValueFrom(this.api.options());
      this._actions.set(options.actions);
      this._actors.set(options.actors);
    } catch {
      // A filter that cannot be populated is a smaller problem than a page that will not open.
      this._actions.set([]);
      this._actors.set([]);
    }
  }

  /**
   * Open one row's evidence.
   *
   * ★★ IT CLEARS THE PREVIOUS DETAIL BEFORE FETCHING. Leaving the old one in place while the next
   * request is in flight shows one row's evidence under another row's heading — on an audit screen
   * that is not a flicker, it is a false attribution.
   */
  async openDetail(id: number): Promise<void> {
    this._detail.set(null);
    this._detailError.set(null);
    this._detailLoading.set(true);

    try {
      this._detail.set(await firstValueFrom(this.api.detail(id)));
    } catch {
      this._detailError.set('AUDIT.DETAIL_ERROR');
    } finally {
      this._detailLoading.set(false);
    }
  }

  closeDetail(): void {
    this._detail.set(null);
    this._detailError.set(null);
  }

  async refresh(): Promise<void> {
    await this.load();
  }

  async clearFilters(): Promise<void> {
    this._filter.set(EMPTY_AUDIT_LOG_FILTER);
    await this.load();
  }

  async goToPage(page: number): Promise<void> {
    await this.load({ page });
  }

  /** ★ Resets to page 1 — same reason as every other list: page 5 of 47 rows stops existing at 100/page. */
  async setPageSize(pageSize: number): Promise<void> {
    await this.load({ pageSize, page: 1 });
  }
}
