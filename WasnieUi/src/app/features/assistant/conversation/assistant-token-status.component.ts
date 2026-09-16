import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { SubscriptionStateService } from '../../subscription/services/subscription-state.service';
import { tokenUsageView } from '../../subscription/token-usage/token-usage';

/**
 * What the workspace has left of its AI tokens, inside the chat (KAN-83 UX pass).
 *
 * ★★ THE WARNING BELONGS HERE, NOT IN SETTINGS. Running out mid-question is where it hurts, and by the time someone
 * opens billing they have already been stopped. Telling them at 80% — in the place they are actually working — is the
 * whole point of moving the sales moment out of the billing screen.
 *
 * ★★ ONE LINE, AND IT STAYS ONE LINE. It sits on top of the conversation, so every pixel it takes is a pixel of chat.
 * A first pass gave it a progress bar and "2,900,000 of 3,000,000" on a second row — it looked considered and ate 66px
 * of the panel. The sentence, the tokens still available and the action fit on a single 36px row; the bar belongs on
 * the billing screen, where there is room for it and nothing to interrupt.
 *
 * ★ IT SHOWS, IT DOES NOT SELL, UNLESS THE USER CAN ACT. The "add tokens" action is gated on `Subscription.Manage` and
 * HIDDEN — not disabled — for anyone else (§5.8): a rep who cannot buy anything is only told why the assistant is
 * about to stop, which is information they can still use.
 */
@Component({
  selector: 'app-assistant-token-status',
  standalone: true,
  imports: [TranslatePipe, IconComponent, HasPermissionPipe],
  template: `
    @if (view(); as v) {
      @if (display() === 'warning') {
        @if (v.nearLimit && !v.exhausted) {
          <div class="token-status" role="status" data-testid="assistant-token-warning">
            <span class="token-status__chip" aria-hidden="true"><app-icon name="zap" [size]="12" /></span>

            <p class="token-status__text">{{ 'ASSISTANT_USAGE.NEAR_LIMIT' | translate }}</p>

            <!--
              What is LEFT, not what is spent: it is the number that decides whether to act. Hidden on a narrow panel
              so the sentence never loses its own line to it.
            -->
            <span class="token-status__figures">
              {{ 'ASSISTANT_USAGE.CHAT_REMAINING' | translate: { tokens: remaining() } }}
            </span>

            @if ('Subscription.Manage' | hasPermission) {
              <button type="button" class="token-status__action" (click)="addTokens.emit()" data-testid="assistant-token-warning-action">
                {{ 'ASSISTANT_USAGE.ADDONS_ACTION' | translate }}
                <app-icon name="arrow-right" [size]="13" />
              </button>
            }
          </div>
        }
      } @else if (!v.exhausted && !v.nearLimit) {
        <!--
          ★ NOT SHOWN WHILE THE WARNING IS UP. The strip above already carries the same number, and saying it twice on
          one screen reads as two different facts.
        -->
        <strong class="token-status__remaining" data-testid="assistant-token-remaining">
          {{ 'ASSISTANT_USAGE.CHAT_REMAINING' | translate: { tokens: remaining() } }}
        </strong>
      }
    }
  `,
  styleUrl: './assistant-token-status.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AssistantTokenStatusComponent {
  private readonly subState = inject(SubscriptionStateService);
  private readonly translate = inject(TranslateService);

  /**
   * Which half of this component to render.
   *
   * ★ ONE SOURCE, TWO PLACES. The warning strip sits ABOVE the composer's footer and the remaining figure sits INSIDE
   * the disclaimer line — two positions in the DOM that a single element cannot occupy. Splitting the rule into two
   * components would be two answers to "how much is left"; this is one rule rendered where each piece belongs.
   */
  readonly display = input<'warning' | 'inline'>('warning');

  readonly addTokens = output<void>();

  /**
   * The same view model the meters on the billing and settings screens read. A second rule here would be a second
   * answer to "are we near the limit?", and the two would disagree the first time either changed.
   */
  readonly view = computed(() => {
    const v = tokenUsageView(this.subState.access());
    return v.kind === 'none' ? null : v;
  });

  /** Tokens still spendable: what is left of the included allowance plus whatever add-on remains. */
  readonly remaining = computed(() => {
    const v = this.view();
    if (!v) return '';

    const included = Math.max(0, v.limit - v.used);
    const addon = v.kind === 'period' ? v.addonRemaining : 0;
    return this.format(included + addon);
  });

  /** Token counts are large: grouped in the reader's own language (1,000,000 / 1.000.000 / 1 000 000). */
  private format(value: number): string {
    return new Intl.NumberFormat(this.translate.currentLang ?? 'en').format(value);
  }
}
