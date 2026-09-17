import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { MyDashboardStore } from './state/my-dashboard.store';
import { MyCurrencyBalance, MyQuotaAttainment } from './models/my-dashboard.model';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { CurrencyFormatPipe } from '../../shared/pipes/currency-format.pipe';
import { DateFormatPipe } from '../../shared/pipes/date-format.pipe';
import { PayeeLedgerPanelComponent } from '../ledger/panel/payee-ledger-panel.component';
import { RefreshOnEnterDirective } from '../../shared/directives/refresh-on-enter.directive';
import {
  WsBadgeComponent,
  WsCardComponent,
  WsEmptyStateComponent,
  WsGaugeComponent,
  WsPageLayoutComponent,
} from '../../shared/ui';

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
    TranslateModule,
    AppShellComponent,
    RefreshOnEnterDirective,
    IconComponent,
    CurrencyFormatPipe,
    DateFormatPipe,
    PayeeLedgerPanelComponent,
    WsBadgeComponent,
    WsCardComponent,
    WsEmptyStateComponent,
    WsGaugeComponent,
    WsPageLayoutComponent,
  ],
  templateUrl: './my-dashboard.component.html',
  styleUrl: './my-dashboard.component.scss',
})
export class MyDashboardComponent implements OnInit {
  readonly store = inject(MyDashboardStore);

  private readonly now = signal(new Date());

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

  ngOnInit(): void {
    void this.store.load();
  }

  /**
   * The four figures of one currency, in reading order.
   *
   * ★ `awaitingPayment` AND `outstandingDebt` ARE BOTH ALL-TIME and the two period figures are not.
   * The labels say which is which — a screen that let them sit unlabelled side by side would invite
   * the subtraction the backend already refuses to do.
   */
  cards(balance: MyCurrencyBalance): ReadonlyArray<{
    key: string;
    labelKey: string;
    value: number;
    tone: 'neutral' | 'owed' | 'debt';
  }> {
    return [
      { key: 'earned', labelKey: 'MY_DASHBOARD.EARNED', value: balance.earnedCommissionsInPeriod, tone: 'neutral' },
      { key: 'paid', labelKey: 'MY_DASHBOARD.PAID', value: balance.paidOutInPeriod, tone: 'neutral' },
      { key: 'awaiting', labelKey: 'MY_DASHBOARD.AWAITING', value: balance.awaitingPaymentAllTime, tone: 'owed' },
      { key: 'debt', labelKey: 'MY_DASHBOARD.DEBT', value: balance.outstandingDebt, tone: 'debt' },
    ];
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

  /** Units targets count deals, so they must never be printed with a currency symbol (section C4). */
  isUnits(quota: MyQuotaAttainment): boolean {
    return quota.measurement === 'Units';
  }
}
