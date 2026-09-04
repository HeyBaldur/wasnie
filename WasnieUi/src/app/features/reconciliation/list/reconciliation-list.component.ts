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
import { ToastService } from '../../../shared/services/toast.service';
import { ReconciliationApiService } from '../services/reconciliation.api.service';
import { ReconciliationStore } from '../state/reconciliation.store';
import { ReconciliationRow } from '../models/reconciliation.model';
import { reasonKey } from '../models/reconciliation-reason';
import {
  resolutionFor,
  noResolutionKey,
  type ReconciliationResolution,
  type ReprocessResolution,
} from '../models/reconciliation-resolution';
import { ProcessPendingComponent } from '../../transactions/process-pending/process-pending.component';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { WsModalComponent } from '../../../shared/ui';
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
  WsTextareaComponent,
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
    WsEmptyStateComponent, WsPaginationComponent, WsModalComponent, WsTextareaComponent,
    HasPermissionPipe, ProcessPendingComponent,
  ],
  templateUrl: './reconciliation-list.component.html',
  styleUrl: './reconciliation-list.component.scss',
})
export class ReconciliationListComponent implements OnInit {
  readonly store = inject(ReconciliationStore);
  private readonly api = inject(ReconciliationApiService);
  private readonly toast = inject(ToastService);
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

  /**
   * Change how many rows a page holds, then put the reader back at the top.
   *
   * ★ THE PAGINATOR EMITS TWO EVENTS AND THIS SCREEN ONLY LISTENED TO ONE. `ws-pagination` has
   * emitted `pageSizeChange` all along; every other list in the app binds it and this one did not,
   * so 10 / 25 / 50 / 100 rendered, highlighted on click, and did nothing. Nothing in a unit test
   * would have caught it: the handler that was missing is the one the template never called.
   */
  async goToPageSize(pageSize: number): Promise<void> {
    await this.store.setPageSize(pageSize);
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

  // ── Resolution (KAN-49) ───────────────────────────────────────────────────

  /**
   * ★ THE MAP IS THE MODEL'S, NOT THIS COMPONENT'S. Keeping it in
   * `reconciliation-resolution.ts` is what let the Paso 0 table be tested without rendering
   * anything, and it is the same separation the reason whitelist already uses.
   */
  readonly resolutionFor = resolutionFor;
  readonly noResolutionKeyFor = noResolutionKey;

  /** The row whose reprocess modal is open, or null. */
  readonly reprocessTarget = signal<ReprocessResolution | null>(null);

  /**
   * ★ THE MODAL IS KEYED SO THE PANEL INSIDE IT IS REBUILT PER ROW. `ProcessPendingComponent` reads
   * its scope inputs ONCE, in ngOnInit, to load the candidate count and the eligible list. Reusing
   * one instance across two rows would show the second payee the first payee's numbers — on a screen
   * about money. The `@if` on the target destroys and recreates it.
   */
  openReprocess(resolution: ReconciliationResolution): void {
    if (resolution.kind === 'reprocess') this.reprocessTarget.set(resolution);
  }

  /**
   * ★★ CLOSING RELOADS THE QUEUE, AND THAT IS THE WHOLE "no marca" CONTRACT. Nothing here marks the
   * row resolved: if the reprocess created a credit, the row stops satisfying
   * `ProcessableWithoutCreditSpec` and the reload simply does not return it. If it created nothing,
   * the row is still there — truthfully. The screen never asserts a fix it did not verify.
   */
  async closeReprocess(): Promise<void> {
    this.reprocessTarget.set(null);
    await this.store.refresh();
  }


  // ── Closing a row by decision (KAN-51) ────────────────────────────────────

  /** The row whose close modal is open, or null. */
  readonly closeTarget = signal<ReconciliationRow | null>(null);

  readonly closeNote = signal('');

  /**
   * ★★ THE SUBMIT IS BLOCKED ON AN EMPTY NOTE, WHICH IS THE TICKET'S FIRST ACCEPTANCE CRITERION.
   * `trim()` matters: a box holding three spaces is an empty justification, and an auditor reading
   * "  " learns nothing about why a row stopped being shown. The server refuses it too — this is the
   * courtesy, `ReconciliationClosure.Create` is the invariant.
   */
  readonly canSubmitClose = computed(() => this.closeNote().trim().length > 0 && !this.store.closing());

  openClose(row: ReconciliationRow): void {
    this.closeNote.set('');
    this.closeTarget.set(row);
  }

  cancelClose(): void {
    this.closeTarget.set(null);
    this.closeNote.set('');
  }

  /**
   * ★★ BOTH OUTCOMES ARE ANNOUNCED. Closing a row removes money from a queue; a confirmation that
   * happens in silence leaves the person unsure whether their decision was recorded, and a failure
   * that happens in silence is worse — the modal simply stays open and looks unresponsive.
   *
   * ★ THE MODAL SURVIVES A FAILURE, WITH THE TEXT STILL IN IT. Closing it on an error would throw
   * away what the person wrote and leave them believing the row was closed.
   *
   * ★ THE ERROR IS A TRANSLATED KEY, NOT THE SERVER'S SENTENCE (§C1). The only failure a person can
   * actually reach here is "this row is no longer open" — somebody else closed or fixed it first —
   * and that is worth saying in their own language, with what to do about it.
   */
  async confirmClose(): Promise<void> {
    const row = this.closeTarget();
    if (!row || !this.canSubmitClose()) return;

    const ok = await this.store.closeRow(row, this.closeNote().trim());

    if (ok) {
      this.toast.show('RECONCILIATION.CLOSE.TOAST_SUCCESS', 'success');
      this.cancelClose();
    } else {
      this.toast.show('RECONCILIATION.CLOSE.TOAST_ERROR', 'error');
    }
  }

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
