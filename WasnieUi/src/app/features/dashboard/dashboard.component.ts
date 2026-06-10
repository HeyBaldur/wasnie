import { Component, inject } from '@angular/core';
import { DecimalPipe, LowerCasePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { CurrencyFormatPipe } from '../../shared/pipes/currency-format.pipe';
import { DashboardStore } from './store/dashboard.store';
import { CurrencyTotal, DashboardTrendPoint } from './models/dashboard.models';
import {
  WsCardComponent,
  WsBadgeComponent,
  WsPageLayoutComponent,
  WsSegmentedControlComponent,
  WsStatCardComponent,
  WsGaugeComponent,
  WsBarChartComponent,
  type SegOption,
  type CardAccent,
  type BarChartPoint,
} from '../../shared/ui';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    RouterLink,
    DecimalPipe,
    LowerCasePipe,
    TranslatePipe,
    AppShellComponent,
    IconComponent,
    CurrencyFormatPipe,
    WsCardComponent,
    WsBadgeComponent,
    WsPageLayoutComponent,
    WsSegmentedControlComponent,
    WsStatCardComponent,
    WsGaugeComponent,
    WsBarChartComponent,
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent {
  readonly store = inject(DashboardStore);

  readonly periodOptions: SegOption[] = [
    { value: 'this-month', label: 'DASHBOARD.PERIOD_THIS_MONTH' },
    { value: 'last-month', label: 'DASHBOARD.PERIOD_LAST_MONTH' },
    { value: 'ytd', label: 'DASHBOARD.PERIOD_YTD' },
    { value: 'all-time', label: 'DASHBOARD.PERIOD_ALL_TIME' },
  ];

  onPeriodChange(value: string): void {
    this.store.setPeriod(value);
  }

  actionCardAccent(count: number): CardAccent {
    return count > 0 ? 'warning' : 'none';
  }

  amountsAccent(totals: CurrencyTotal[]): CardAccent {
    return totals.length > 0 ? 'warning' : 'none';
  }

  trendIcon(direction: 'up' | 'down' | 'neutral'): string {
    return direction === 'neutral' ? 'trend-neutral' : 'trend-up';
  }

  /** True when the change% is either null (prior=0) or absurdly large (near-zero prior). */
  trendIsNoBase(point: DashboardTrendPoint): boolean {
    return point.changePercent === null || Math.abs(point.changePercent) > 500;
  }

  /** Safe formatted change % — only call when trendIsNoBase() returns false. */
  trendChangeFormatted(point: DashboardTrendPoint): string {
    const v = point.changePercent!;
    return `${v > 0 ? '+' : ''}${v.toFixed(1)}%`;
  }

  /** Two-bar chart data: [current, prior] for a trend point. */
  trendBarPoints(point: DashboardTrendPoint, currentLabel: string, priorLabel: string): BarChartPoint[] {
    return [
      { label: priorLabel, value: point.priorAmount, currency: point.currency },
      { label: currentLabel, value: point.currentAmount, currency: point.currency, isCurrent: true },
    ];
  }

  /** "admin@domain.com" → "admin" (max 18 chars, then ellipsis). */
  actorShortName(email: string): string {
    const at = email.indexOf('@');
    const name = at > 0 ? email.slice(0, at) : email;
    return name.length > 18 ? `${name.slice(0, 17)}…` : name;
  }

  /** "pending_transactions_processed" → "pending transactions" (max 3 words). */
  formatActivityAction(raw: string): string {
    return raw.replace(/_/g, ' ').split(' ').slice(0, 3).join(' ');
  }

  /** Truncate resource display name to 28 chars. */
  shortResource(name: string | null): string | null {
    if (!name) return null;
    return name.length > 28 ? `${name.slice(0, 26)}…` : name;
  }

  /** Total count of action items needing attention (for band header badge). */
  pendingActionCount(): number {
    const b = this.store.actionBand();
    if (!b) return 0;
    return (
      b.draftPayRunsCount +
      b.payoutsPendingApprovalCount +
      (b.payoutsApprovedUnpaidByCurrency.length > 0 ? 1 : 0)
    );
  }

  relativeTime(isoUtc: string): string {
    const diff = Date.now() - new Date(isoUtc).getTime();
    const mins = Math.floor(diff / 60_000);
    if (mins < 60) return `${Math.max(0, mins)}m`;
    const hours = Math.floor(mins / 60);
    if (hours < 24) return `${hours}h`;
    return `${Math.floor(hours / 24)}d`;
  }

  trackByCurrency(_: number, item: CurrencyTotal): string {
    return item.currency;
  }

  attainmentGaugeValue(): number {
    const pct = this.store.periodBand()?.avgQuotaAttainmentPercent;
    if (pct === null || pct === undefined) return 0;
    return pct / 100;
  }
}
