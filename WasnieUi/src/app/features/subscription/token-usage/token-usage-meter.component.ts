import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { AccountAccess } from '../services/subscription.service';
import { tokenUsageView } from './token-usage';

/**
 * The assistant's token usage for the account (KAN-80): "used of limit" with a bar during the trial, "used this billing
 * period" once paying. ONE component, rendered in Settings and on the billing screen, so the two can never disagree.
 *
 * ★ THE NUMBERS ARRIVE FROM THE SERVER. The component formats them and draws a share; it adds nothing up — the sum the
 * meter shows is the same one that refuses a turn at the limit.
 *
 * ★ THE BAR IS AN SVG WITH ITS WIDTH BOUND AS AN ATTRIBUTE, the technique the billing screen's ring already uses. There is
 * no progress-bar primitive, and a `[style.width.%]` binding would be the inline style §5.5 forbids.
 */
@Component({
  selector: 'app-token-usage-meter',
  standalone: true,
  imports: [TranslatePipe, DateFormatPipe],
  templateUrl: './token-usage-meter.component.html',
  styleUrl: './token-usage-meter.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TokenUsageMeterComponent {
  private readonly translate = inject(TranslateService);

  readonly access = input.required<AccountAccess | null>();

  /** `compact` sits inside another card's text (billing); `full` is its own block (settings). */
  readonly variant = input<'full' | 'compact'>('full');

  readonly view = computed(() => tokenUsageView(this.access()));

  /** Width of the filled part of the bar, in the SVG's 0..100 viewBox units. */
  readonly barWidth = computed(() => {
    const v = this.view();
    return v.kind === 'trial' ? Math.round(v.fraction * 1000) / 10 : 0;
  });

  /** Token counts are large: grouped in the reader's own language (1,000,000 / 1.000.000 / 1 000 000). */
  format(value: number): string {
    return new Intl.NumberFormat(this.translate.currentLang ?? 'en').format(value);
  }
}
