import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ReconciliationApiService } from '../services/reconciliation.api.service';
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
}
