import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { MyDashboardStore } from './state/my-dashboard.store';
import { MyCurrencyBalance, MyQuotaAttainment } from './models/my-dashboard.model';
import { TranslateService } from '@ngx-translate/core';
import type { BarChartPoint } from '../../shared/ui/ws-bar-chart/ws-bar-chart.component';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { CurrencyFormatPipe } from '../../shared/pipes/currency-format.pipe';
import { formatCompactMoney } from '../../shared/utils/money-compact';
import { DateFormatPipe } from '../../shared/pipes/date-format.pipe';
import { PayeeLedgerPanelComponent } from '../ledger/panel/payee-ledger-panel.component';
import { RefreshOnEnterDirective } from '../../shared/directives/refresh-on-enter.directive';
import {
  WsBadgeComponent,
  WsCardComponent,
  WsDateRangePickerComponent,
  WsEmptyStateComponent,
  WsHBarChartComponent,
  WsPageLayoutComponent,
  type DateRange,
} from '../../shared/ui';
import { currentMonthRange } from '../dashboard/store/dashboard.store';

/**
 * The personal dashboard (KAN-92, batch 3) — what somebody who gets paid sees when they sign in.
 *
 * ★★ IT REPLACES A SCREEN OF ZEROS. The company dashboard needs Reports.ViewAll, so a rep landing
 * there read every figure as 0 and could only conclude the product was broken or that they had earned
 * nothing. This screen answers the question they actually have: what have I earned, what am I owed,
 * and how am I doing against my target.
 *
 * ★★ NOT LINKED IS SAID OUT LOUD, NOT DRAWN AS ZERO. When no payee record is attached to this login
 * the screen says so and names what to do about it (section C3). Every other state — loading, a
 * failed request — is also distinguished, because all three would otherwise look like "nothing".
 *
 * ★★ NO ARITHMETIC HAPPENS HERE. Every figure is a field the server computed; the component formats
 * and orders, nothing more. The ledger movements reuse the panel the payee page already mounts (§5.1)
 * rather than growing a second rendering of the same entries, and the adjustment form inside it is
 * already hidden from anybody without Ledger.Adjust.
 */
@Component({
  selector: 'app-my-dashboard',
  standalone: true,
  imports: [
    DecimalPipe,
    ReactiveFormsModule,
    TranslateModule,
    AppShellComponent,
    RefreshOnEnterDirective,
    IconComponent,
    CurrencyFormatPipe,
    DateFormatPipe,
    PayeeLedgerPanelComponent,
    WsBadgeComponent,
    WsCardComponent,
    WsDateRangePickerComponent,
    WsEmptyStateComponent,
    WsHBarChartComponent,
    WsPageLayoutComponent,
  ],
  templateUrl: './my-dashboard.component.html',
  styleUrl: './my-dashboard.component.scss',
})
export class MyDashboardComponent {
  readonly store = inject(MyDashboardStore);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly now = signal(new Date());

  /**
   * Placeholders while the figures are in flight — one per card, so the grid does not reflow when the
   * real numbers land. The reader sees the shape of the answer before the answer.
   */
  readonly loadingSlots = [0, 1, 2, 3];

  readonly greetingKey = computed(() => {
    const hour = this.now().getHours();
    if (hour < 12) return 'DASHBOARD.GREETING_MORNING';
    if (hour < 18) return 'DASHBOARD.GREETING_AFTERNOON';
    return 'DASHBOARD.GREETING_EVENING';
  });

  /** Carries its own comma so the greeting still reads correctly when there is no name. */
  readonly greetingNamePart = computed(() => {
    const name = this.store.payeeName();
    return name ? `, ${name}` : '';
  });

  /**
   * KAN-98 - the window control. `WsDateRangePicker` is a ControlValueAccessor, so the binding goes
   * through a FormControl exactly as it does on the company dashboard and on the payee page (5.1).
   * Seeded from the store so it opens showing the range the figures were already requested for.
   */
  readonly rangeControl = new FormControl<DateRange>(
    { start: this.store.range().from, end: this.store.range().to },
    { nonNullable: true }
  );

  constructor() {
    this.rangeControl.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(value => this.onRangeChange(value));
  }

  /**
   * THE FIRST LOAD IS NOT FIRED HERE ANY MORE, and this is the part that is easy to get wrong. The
   * store now loads from an effect on its own range signal, so an `ngOnInit` calling `load()` as well
   * would put two requests for the same window on the wire and leave whichever answered last on screen.
   * The screen asks for data by SETTING THE RANGE; there is no second way in.
   */
  onRangeChange(value: DateRange): void {
    // The picker orders the two ends itself, the store refuses a backwards range and the server refuses
    // it again. Three layers, because this window decides what money the page reports.
    if (!value?.start || !value?.end) return;
    this.store.setRange({ from: value.start, to: value.end });
  }

  /** Back to the window the screen opens on, without a page reload. */
  resetRange(): void {
    const range = currentMonthRange();
    this.rangeControl.setValue({ start: range.from, end: range.to });
  }

  /**
   * The four figures of one currency, in reading order, each with the sentence that says what it is.
   *
   * ★★ THE FOUR ARE NOT READ AS A SUM, and the descriptions are what stop them being read as one.
   * "Paid" is a part of "Earned"; "Awaiting" is the rest of it; "You owe" moves in the opposite
   * direction entirely. A row of four bare numbers invites exactly the arithmetic the backend refuses
   * to do on the reader's behalf.
   *
   * ★ NONE OF THEM IS COLOURED, deliberately — the same rule the company dashboard's three commission
   * cards follow. Painting the debt red states a judgement the figure does not carry: owing money
   * against future commission is ordinary, and the screen says what the situation IS in the sentence
   * underneath, where it can be phrased instead of implied.
   */
  cards(balance: MyCurrencyBalance): ReadonlyArray<{
    key: string;
    labelKey: string;
    descKey: string;
    value: number;
    scopeKey: string;
  }> {
    return [
      { key: 'earned', labelKey: 'MY_DASHBOARD.EARNED', descKey: 'MY_DASHBOARD.EARNED_DESC', value: balance.earnedCommissionsInPeriod, scopeKey: 'MY_DASHBOARD.SCOPE_RANGE' },
      { key: 'paid', labelKey: 'MY_DASHBOARD.PAID', descKey: 'MY_DASHBOARD.PAID_DESC', value: balance.paidOutInPeriod, scopeKey: 'MY_DASHBOARD.SCOPE_RANGE' },
      { key: 'awaiting', labelKey: 'MY_DASHBOARD.AWAITING', descKey: 'MY_DASHBOARD.AWAITING_DESC', value: balance.awaitingPaymentAllTime, scopeKey: 'MY_DASHBOARD.SCOPE_ALL_TIME' },
      { key: 'debt', labelKey: 'MY_DASHBOARD.DEBT', descKey: 'MY_DASHBOARD.DEBT_DESC', value: balance.outstandingDebt, scopeKey: 'MY_DASHBOARD.SCOPE_TODAY' },
    ];
  }

  /**
   * The window the figures on screen were built over, as one line: "1 Jul 2026 - 31 Jul 2026".
   *
   * IT READS THE APPLIED RANGE, NOT THE PICKER. While a request is in flight the two differ, and that
   * gap is precisely when somebody glances at the heading - a label driven by the control would relabel
   * July's pay as August's a full round-trip before the money underneath it changed.
   *
   * Empty until the server has answered, so the chip is simply absent rather than guessing.
   */
  readonly appliedRangeLabel = computed(() => {
    const r = this.store.appliedRange();
    if (!r) return '';

    const fmt = (iso: string) => {
      // Parsed as local midnight, never `new Date(iso)`: that reads a bare yyyy-MM-dd as UTC, so a
      // reader west of Greenwich sees every bound a day early.
      const [y, m, d] = iso.split('-').map(Number);
      return new Date(y, m - 1, d).toLocaleDateString(this.translate.currentLang || 'en', {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
      });
    };

    return `${fmt(r.from)} - ${fmt(r.to)}`;
  });

  /** Compact notation for the leading figure. The exact amount is printed underneath it, always. */
  fmtCompact(amount: number, currency: string): string {
    return formatCompactMoney(amount, currency);
  }

  /**
   * The sentence under the figures, chosen by the token the server sent.
   *
   * ★ A WHITELIST, NOT A CONCATENATED KEY (section C2). An unknown token must not print an internal
   * identifier on somebody's pay screen; it falls back to a generic line instead.
   */
  interpretationKey(balance: MyCurrencyBalance): string {
    switch (balance.interpretation) {
      case 'NothingRecorded': return 'MY_DASHBOARD.STATE_NOTHING_RECORDED';
      case 'EarningsAndNoDebt': return 'MY_DASHBOARD.STATE_EARNINGS_NO_DEBT';
      case 'EarningsWithDebt': return 'MY_DASHBOARD.STATE_EARNINGS_WITH_DEBT';
      case 'DebtOnly': return 'MY_DASHBOARD.STATE_DEBT_ONLY';
      case 'DebtExceedsPending': return 'MY_DASHBOARD.STATE_DEBT_EXCEEDS';
      default: return 'MY_DASHBOARD.STATE_UNKNOWN';
    }
  }

  /**
   * ★★ A QUOTA WITH NO TARGET IS NOT 0% ATTAINMENT, and the gauge must not draw it as a failure. The
   * engine sends the reason next to the ratio precisely so this screen can tell the person "nobody
   * set you a target" instead of showing them a red zero they cannot act on.
   */
  hasTarget(quota: MyQuotaAttainment): boolean {
    return quota.attainmentSource !== 'NoTarget';
  }


  /**
   * The two bars behind a quota: what was asked for, and what has happened.
   *
   * ★★ `isCurrent` IS NOT COSMETIC — it is how `ws-hbar-chart` decides which bar is the live one. It
   * picks exactly one "prior" and one "current" (`pts.find(p => !p.isCurrent)` and its opposite), so
   * ACHIEVED carries the flag and gets the gradient that glows, while TARGET sits behind it in
   * neutral. Flip the flags and the chart says the opposite thing.
   *
   * ★ NO CURRENCY ON A UNITS QUOTA (§C4). A Units target counts deals; the chart formats a point with
   * a currency symbol whenever one is present, so passing one would print "€40" over a target of 40
   * sales — a number lying about its own unit.
   */
  attainmentPoints(quota: MyQuotaAttainment): BarChartPoint[] {
    const currency = this.isUnits(quota) ? undefined : quota.currency;

    return [
      { label: this.translate.instant('ATTAINMENT.TARGET'), value: quota.targetAmount, currency },
      { label: this.translate.instant('ATTAINMENT.ACHIEVED'), value: quota.achievedAmount, currency, isCurrent: true },
    ];
  }

  /** The figure in the pill: how far along the target this person is, rounded. */
  pctOfTarget(quota: MyQuotaAttainment): number {
    return Math.round(quota.attainmentRatio * 100);
  }

  /**
   * ★ THE PILL'S COLOUR IS A VERDICT, SO IT ONLY SPEAKS WHEN IT IS SURE. Green at or above target,
   * red below half, and the neutral default in between — where "behind" and "on track" are a matter
   * of how much of the period is gone, which this card does not know.
   */
  pillClass(quota: MyQuotaAttainment): string {
    const pct = this.pctOfTarget(quota);
    if (pct >= 100) return 'trend-card__delta-pill--up';
    if (pct < 50) return 'trend-card__delta-pill--down';
    return '';
  }

  /** Units targets count deals, so they must never be printed with a currency symbol (section C4). */
  isUnits(quota: MyQuotaAttainment): boolean {
    return quota.measurement === 'Units';
  }
}
