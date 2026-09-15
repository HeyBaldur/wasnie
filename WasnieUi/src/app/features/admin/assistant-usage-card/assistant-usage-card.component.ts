import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { SubscriptionStateService } from '../../subscription/services/subscription-state.service';
import { TokenUsageMeterComponent } from '../../subscription/token-usage/token-usage-meter.component';
import { tokenUsageView } from '../../subscription/token-usage/token-usage';

/**
 * Settings → the assistant's token usage for this account (KAN-80).
 *
 * Reads the account access the shell already loaded — no request of its own — and renders nothing when there is no usage
 * the account can act on (a locked account, or before the access state has arrived).
 */
@Component({
  selector: 'app-assistant-usage-card',
  standalone: true,
  imports: [TranslatePipe, TokenUsageMeterComponent],
  template: `
    @if (visible()) {
      <section class="settings-card" data-testid="assistant-usage-card">
        <h3 class="settings-section-title">{{ 'ASSISTANT_USAGE.TITLE' | translate }}</h3>
        <p class="settings-section-desc">{{ 'ASSISTANT_USAGE.DESC' | translate }}</p>
        <app-token-usage-meter [access]="subState.access()" />
      </section>
    }
  `,
  styleUrl: './assistant-usage-card.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AssistantUsageCardComponent {
  readonly subState = inject(SubscriptionStateService);

  readonly visible = computed(() => tokenUsageView(this.subState.access()).kind !== 'none');
}
