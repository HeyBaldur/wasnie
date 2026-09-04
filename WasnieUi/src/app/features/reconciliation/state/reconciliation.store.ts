import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ReconciliationApiService } from '../services/reconciliation.api.service';
import { SidebarBadgesStore } from '../../../core/navigation/sidebar-badges.store';
import {
  EMPTY_RECONCILIATION_FILTER,
  ReconciliationFilter,
  ReconciliationPage,
  ReconciliationRow,
  ReconciliationSummary,
} from '../models/reconciliation.model';

const EMPTY_SUMMARY: ReconciliationSummary = { totalRows: 0, byCurrency: [], byReason: [] };

@Injectable({ providedIn: 'root' })
export class ReconciliationStore {
  private readonly api = inject(ReconciliationApiService);
  private readonly sidebarBadges = inject(SidebarBadgesStore);

  private readonly _rows = signal<readonly ReconciliationRow[]>([]);
  private readonly _summary = signal<ReconciliationSummary>(EMPTY_SUMMARY);
  private readonly _total = signal(0);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _filter = signal<ReconciliationFilter>(EMPTY_RECONCILIATION_FILTER);
  private readonly _reasons = signal<readonly string[]>([]);

  readonly rows = this._rows.asReadonly();

  /**
   * ★★ THE CARDS READ THIS, AND THIS COMES OFF THE WIRE. It is never derived from `rows()`. The
   * rows are one page; the summary describes the entire filtered set, computed by the same query on
   * the server. Summing the page here would produce a number that shrinks as you paginate.
   */
  readonly summary = this._summary.asReadonly();

  readonly total = this._total.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly filter = this._filter.asReadonly();
  readonly reasons = this._reasons.asReadonly();

  readonly totalPages = computed(() => {
    const size = this._filter().pageSize;
    return size > 0 ? Math.max(1, Math.ceil(this._total() / size)) : 1;
  });

  readonly isEmpty = computed(() => !this._loading() && this._rows().length === 0);

  readonly activeFilterCount = computed(() => {
    const f = this._filter();
    return [f.payeeId, f.reason, f.from, f.to].filter((v) => v !== null && v !== '').length;
  });

  readonly hasActiveFilters = computed(() => this.activeFilterCount() > 0);

  /** Any money at all in the filtered set — decides whether the money cards are worth showing. */
  readonly hasMoney = computed(() => this._summary().byCurrency.length > 0);


  // ── Closing a row by decision (KAN-51) ──────────────────────────────────────

  private readonly _closing = signal(false);

  readonly closing = this._closing.asReadonly();

  /**
   * Record the decision, then RELOAD.
   *
   * ★★ IT DOES NOT SPLICE THE ROW OUT OF `_rows`. Removing it locally would leave the money cards —
   * which the server computed over the whole filtered set — describing a row the table no longer
   * shows, and the guarantee this screen exists for is that the two agree. The reload is one request
   * and it keeps them the same query.
   *
   * ★ IT RETURNS WHETHER IT WORKED, so the modal stays open on failure with the note still in the
   * box. Closing the modal on an error would lose what the person wrote.
   *
   * ★ THE MESSAGE IS THE COMPONENT'S, NOT THE STORE'S. Every action in this app reports through a
   * toast raised by the screen (assignments, plans, transactions); a second error signal here would
   * be a second place the same failure is announced, and the two would drift.
   */
  async closeRow(row: ReconciliationRow, note: string): Promise<boolean> {
    this._closing.set(true);

    try {
      await firstValueFrom(this.api.close({ kind: row.kind, entityId: row.entityId, note }));
      await this.load();

      // ★ THE BADGE IS TOLD, NOT LEFT TO NOTICE. Closing a row is precisely an action that changes the
      // sidebar's count; without this the number would stay wrong until the five-minute timer, and the
      // user would be looking straight at the proof that it is wrong.
      void this.sidebarBadges.refresh();
      return true;
    } catch {
      return false;
    } finally {
      this._closing.set(false);
    }
  }

  async load(filter?: Partial<ReconciliationFilter>): Promise<void> {
    const next = { ...this._filter(), ...filter };
    this._filter.set(next);
    this._loading.set(true);
    this._error.set(null);

    try {
      const page: ReconciliationPage = await firstValueFrom(this.api.list(next));
      this._rows.set(page.items);
      this._summary.set(page.summary);
      this._total.set(page.totalCount);
    } catch {
      this._error.set('RECONCILIATION.LOAD_ERROR');
      this._rows.set([]);
      this._summary.set(EMPTY_SUMMARY);
      this._total.set(0);
    } finally {
      this._loading.set(false);
    }
  }

  async loadReasons(): Promise<void> {
    try {
      this._reasons.set(await firstValueFrom(this.api.reasons()));
    } catch {
      // A filter that cannot be populated is a smaller problem than a page that will not open.
      this._reasons.set([]);
    }
  }

  async refresh(): Promise<void> {
    await this.load();
  }

  async clearFilters(): Promise<void> {
    this._filter.set(EMPTY_RECONCILIATION_FILTER);
    await this.load();
  }

  async goToPage(page: number): Promise<void> {
    await this.load({ page });
  }

  /**
   * ★★ IT RESETS TO PAGE 1, AND THAT IS NOT A DETAIL. Growing the page size shrinks the number of
   * pages: sitting on page 5 of 47 rows at 10-per-page and switching to 100 leaves the reader on a
   * page that no longer exists, and the server answers with nothing. Every other list in the app
   * resets — assignments, credits, transactions, category-mappings — and this one now agrees with
   * them instead of inventing a fifth behaviour.
   */
  async setPageSize(pageSize: number): Promise<void> {
    await this.load({ pageSize, page: 1 });
  }
}
