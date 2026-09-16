import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, signal } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { WsButtonComponent, WsModalComponent } from '../../../shared/ui';
import { cardBrandLabel } from '../billing/billing-display';
import { checkoutNavigator } from '../plan-offer/start-checkout';
import { BoostOffer, SubscriptionService } from '../services/subscription.service';

/** Where the purchase was started from, so Stripe returns the buyer to that screen. A closed set, never a URL. */
export type AddTokensReturnTo = 'Billing' | 'Assistant';

/** One pack, ready to paint. The arithmetic happens here, never in the template (§5.7). */
export interface TokenPackView {
  priceId: string;
  tokens: string;
  rawTokens: number;
  amount: number;
  currency: string;
  perMillion: number;
  bestValue: boolean;
}

/**
 * Turns the offers Stripe returns into what the dialog draws.
 *
 * ★ "BEST VALUE" IS DERIVED, NOT DECLARED: the lowest price per million, computed from the live prices. Repricing a
 * pack in Stripe moves the label on its own; a flag somebody maintains is a claim that silently stops being true.
 */
export function tokenPackViews(offers: readonly BoostOffer[], locale: string): TokenPackView[] {
  const priced = offers.map(o => ({
    priceId: o.priceId,
    tokens: new Intl.NumberFormat(locale).format(o.tokens),
    rawTokens: o.tokens,
    amount: o.amountCents / 100,
    currency: o.currency,
    perMillion: o.tokens > 0 ? o.amountCents / 100 / (o.tokens / 1_000_000) : 0,
  }));

  // A single pack is not "the best value" of anything — the label only means something against an alternative.
  const best = priced.length > 1 ? Math.min(...priced.map(p => p.perMillion)) : Number.NEGATIVE_INFINITY;

  return priced.map(p => ({ ...p, bestValue: p.perMillion === best }));
}

/**
 * Buying additional AI tokens, as a dialog (KAN-83 UX pass).
 *
 * ★ ONE DIALOG, TWO ENTRY POINTS. Billing opens it from a quiet row; the assistant opens it from the card that just
 * stopped a question. Both get the identical flow — a second, chat-only purchase screen would be a second place for
 * the price, the confirmation and the wording to drift.
 *
 * ★★ MONEY ASKS TWICE. Picking a pack does NOT start a payment: it moves to a confirmation step that names the size,
 * the price and the card it will be charged to. Nothing leaves for Stripe until the customer confirms there. A
 * single-click purchase on a surface someone reached by mis-clicking a chat alert is how an accidental charge happens.
 *
 * ★ NOTHING IS CLAIMED ABOUT THE CHARGE THAT IS NOT TRUE. The final confirmation still happens on Stripe's own page —
 * that is the flow this product has — so the dialog says so instead of implying the money moves when the button is
 * pressed.
 */
@Component({
  selector: 'app-add-tokens-dialog',
  standalone: true,
  imports: [TranslatePipe, CurrencyFormatPipe, IconComponent, WsModalComponent, WsButtonComponent],
  templateUrl: './add-tokens-dialog.component.html',
  styleUrl: './add-tokens-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddTokensDialogComponent {
  private readonly subscriptionService = inject(SubscriptionService);
  private readonly translate = inject(TranslateService);

  /** Two-way, so either screen opens and closes it with a single binding. */
  readonly open = model(false);

  readonly returnTo = input<AddTokensReturnTo>('Billing');

  readonly offers = signal<BoostOffer[]>([]);
  readonly loading = signal(false);
  readonly failed = signal(false);

  /** The pack awaiting confirmation. Null means the dialog is still on the choosing step. */
  readonly selected = signal<TokenPackView | null>(null);
  readonly submitting = signal(false);

  readonly paymentBrand = signal<string | null>(null);
  readonly paymentLast4 = signal<string | null>(null);

  readonly packs = computed(() => tokenPackViews(this.offers(), this.translate.currentLang ?? 'en'));

  /**
   * What is true of every add-on, whichever pack is bought.
   *
   * ★ EACH LINE IS A PROPERTY OF THE FEATURE, NOT A SALES CLAIM: the one-off payment, the year's expiry, the pool
   * shared by the whole workspace and the included-first order are all decisions the code actually implements.
   */
  readonly notes = [
    'ASSISTANT_USAGE.ADDON_NOTE_ONE_OFF',
    'ASSISTANT_USAGE.ADDON_NOTE_ROLLS_OVER',
    'ASSISTANT_USAGE.ADDON_NOTE_SHARED',
    'ASSISTANT_USAGE.ADDON_NOTE_INCLUDED_FIRST',
  ];

  readonly unavailable = computed(() => !this.loading() && this.packs().length === 0);

  /** Placeholder rows for the loading skeleton: three packs and four notes, the shape of the real answer. */
  readonly ghosts = [0, 1, 2];
  readonly ghosts4 = [0, 1, 2, 3];

  constructor() {
    // Loaded when it opens, not on every host render: the price list is a live Stripe read and most sessions never
    // open this dialog at all.
    effect(() => {
      if (this.open()) {
        this.load();
      } else {
        this.selected.set(null);
        this.submitting.set(false);
        this.failed.set(false);
      }
    });
  }

  private load(): void {
    this.loading.set(true);
    this.failed.set(false);

    this.subscriptionService.getBoostOffers().subscribe({
      next: o => {
        this.offers.set(o);
        this.loading.set(false);
      },
      error: () => {
        this.offers.set([]);
        this.loading.set(false);
      },
    });

    // The card on file, for the confirmation line. Its absence is not an error: Stripe will ask for one.
    this.subscriptionService.getBillingDetails().subscribe({
      next: d => {
        this.paymentBrand.set(cardBrandLabel(d.paymentMethod?.brand));
        this.paymentLast4.set(d.paymentMethod?.last4 ?? null);
      },
      error: () => {
        this.paymentBrand.set(null);
        this.paymentLast4.set(null);
      },
    });
  }

  choose(pack: TokenPackView): void {
    this.failed.set(false);
    this.selected.set(pack);
  }

  back(): void {
    this.selected.set(null);
  }

  confirm(): void {
    const pack = this.selected();
    if (!pack || this.submitting()) return;

    this.submitting.set(true);
    this.failed.set(false);

    this.subscriptionService.createBoostCheckout(pack.priceId, this.returnTo()).subscribe({
      // ★ THE BALANCE IS NOT TOUCHED HERE. It moves when Stripe's webhook credits the purchase; the return trip
      // reloads it. Adding tokens optimistically would show tokens nobody has paid for yet.
      next: r => checkoutNavigator.go(r.checkoutUrl),
      error: () => {
        this.submitting.set(false);
        this.failed.set(true);
      },
    });
  }

  close(): void {
    this.open.set(false);
  }
}
