import { Component, computed, DestroyRef, ElementRef, inject, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { RefreshOnEnterDirective } from '../../../shared/directives/refresh-on-enter.directive';
import { ReconciliationApiService } from '../services/reconciliation.api.service';
import { ReconciliationStore } from '../state/reconciliation.store';
import { ReconciliationRow } from '../models/reconciliation.model';
import { reasonKey } from '../models/reconciliation-reason';
import { findScrollContainer } from '../../../shared/utils/find-scroll-container';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsCardComponent,
  WsSelectComponent,
  WsDatePickerComponent,
  WsPageLayoutComponent,
  WsTableComponent,
  WsTableEmptyComponent,
  WsEmptyStateComponent,
  WsPaginationComponent,
  type SelectOption,
} from '../../../shared/ui';

/**
 * The Reconciliation Centre: every piece of earned money the system could not turn into a payment.
 *
 * ★★ THE CARDS COME FROM THE SERVER, NOT FROM `store.rows()`. This is the whole point of the screen:
 * a CFO reads a total here and says "I owe exactly this". A card summed in the browser would
 * describe the 25 rows currently on screen, and would shrink as somebody paged — a number that
 * looks authoritative and is not. See ReconciliationStore.summary.
 *
 * ★ READ-ONLY, DELIBERATELY. v1 shows and exports; nothing here resolves, forces or carries over.
 */
@Component({
  selector: 'app-reconciliation-list',
  standalone: true,
  imports: [
    AppShellComponent, RefreshOnEnterDirective, RouterLink, ReactiveFormsModule, TranslateModule, DecimalPipe,
    IconComponent, DateFormatPipe, CurrencyFormatPipe,
    WsButtonComponent, WsBadgeComponent, WsCardComponent,
    WsSelectComponent, WsDatePickerComponent,
    WsPageLayoutComponent, WsTableComponent, WsTableEmptyComponent,
    WsEmptyStateComponent, WsPaginationComponent,
  ],
  templateUrl: './reconciliation-list.component.html',
  styleUrl: './reconciliation-list.component.scss',
})
export class ReconciliationListComponent implements OnInit {
  readonly store = inject(ReconciliationStore);
  private readonly api = inject(ReconciliationApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  readonly filterOpen = signal(false);
  readonly exporting = signal(false);
  readonly exportError = signal<string | null>(null);

  readonly form = new FormGroup({
    reason: new FormControl<string | null>(null),
    from: new FormControl<string | null>(null),
    to: new FormControl<string | null>(null),
  });

  /** The filter's options come from the API, so a reason the engine gained is filterable at once. */
  readonly reasonOptions = computed<SelectOption[]>(() => [
    { value: '', label: 'RECONCILIATION.FILTER.ALL_REASONS' },
    ...this.store.reasons().map((code) => ({ value: code, label: reasonKey(code) })),
  ]);

  /** Translation key for a reason code. Whitelisted — never the raw code (§C2). */
  readonly reasonKeyFor = reasonKey;

  ngOnInit(): void {
    void this.store.loadReasons();
    void this.store.load({ page: 1 });

    this.form.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        void this.store.load({
          reason: value.reason || null,
          from: value.from || null,
          to: value.to || null,
          page: 1,
        });
      });
  }

  /**
   * Turn the page, then put the reader back at the top of it.
   *
   * ★★ THE PAGE SCROLLS INSIDE app-shell's COLUMN, NOT THE WINDOW, so `window.scrollTo(0, 0)` does
   * nothing here — that is the trap this app has documented before (see the manual's `jumpTo`). The
   * scroller is found by walking up from the table and taking the first ancestor that actually
   * overflows, so this keeps working if the shell's markup changes.
   *
   * ★ IT SCROLLS AFTER THE ROWS ARRIVE. Scrolling first and loading second lands the reader at the
   * top of the OLD page for as long as the request takes, which reads as the button having done
   * nothing — which is exactly the complaint this fixes.
   */
  async goToPage(page: number): Promise<void> {
    await this.store.goToPage(page);
    this.scrollToTop();
  }

  private scrollToTop(): void {
    const table = this.host.nativeElement.querySelector<HTMLElement>('.ws-table-wrap');
    const scroller = findScrollContainer(table?.parentElement ?? null);

    // ★ INSTANT, NOT SMOOTH, AND THAT IS THE BUG THIS HAD. A smooth scroll is an animation, and
    // Angular repaints twenty-five new rows into this very scroller a frame later — Chrome cancels
    // an in-flight smooth scroll when its content changes, so the reader was left exactly where they
    // were, which is the complaint. Verified in the browser: with 'smooth' the scroller stayed
    // pinned at its maximum; without it, the scroller lands on 0.
    scroller?.scrollTo({ top: 0 });
  }

  clearFilters(): void {
    this.form.reset({ reason: null, from: null, to: null }, { emitEvent: false });
    void this.store.clearFilters();
  }

  trackRow = (_: number, row: ReconciliationRow): string => `${row.kind}:${row.entityId}`;

  /**
   * ★ THE FILE IS THE FILTERED SET, and the service asks for it without a page. The blob is turned
   * into a download here because the browser is the only place that can.
   */
  async exportToExcel(): Promise<void> {
    this.exporting.set(true);
    this.exportError.set(null);
    try {
      const blob = await firstValueFrom(this.api.exportToExcel(this.store.filter()));
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `reconciliation-${new Date().toISOString().slice(0, 10)}.xlsx`;
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      this.exportError.set('RECONCILIATION.EXPORT_ERROR');
    } finally {
      this.exporting.set(false);
    }
  }
}
