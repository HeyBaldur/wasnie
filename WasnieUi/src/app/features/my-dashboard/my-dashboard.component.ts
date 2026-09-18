import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { MyDashboardStore } from './state/my-dashboard.store';
import { MyCurrencyBalance, MyQuotaAttainment } from './models/my-dashboard.model';
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
    DecimalPipe,
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

  ngOnInit(): void {
    void this.store.load();
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
  }> {
    return [
      { key: 'earned', labelKey: 'MY_DASHBOARD.EARNED', descKey: 'MY_DASHBOARD.EARNED_DESC', value: balance.earnedCommissionsInPeriod },
      { key: 'paid', labelKey: 'MY_DASHBOARD.PAID', descKey: 'MY_DASHBOARD.PAID_DESC', value: balance.paidOutInPeriod },
      { key: 'awaiting', labelKey: 'MY_DASHBOARD.AWAITING', descKey: 'MY_DASHBOARD.AWAITING_DESC', value: balance.awaitingPaymentAllTime },
      { key: 'debt', labelKey: 'MY_DASHBOARD.DEBT', descKey: 'MY_DASHBOARD.DEBT_DESC', value: balance.outstandingDebt },
    ];
  }

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

  /** Units targets count deals, so they must never be printed with a currency symbol (section C4). */
  isUnits(quota: MyQuotaAttainment): boolean {
    return quota.measurement === 'Units';
  }
}
