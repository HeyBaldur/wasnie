import { Component, input, output } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { WsButtonComponent } from '../../../../shared/ui';
import { TransactionUpdateResult } from '../models/transaction-update.models';

@Component({
  selector: 'app-tx-update-complete-step',
  standalone: true,
  imports: [TranslateModule, WsButtonComponent],
  template: `
    <div class="update-complete">
      <div class="update-complete__icon">✓</div>
      <h3 class="update-complete__title">{{ 'IMPORTS.UPDATE.COMPLETE_TITLE' | translate }}</h3>
      @if (result(); as r) {
        <div class="update-complete__stats">
          <p>{{ 'IMPORTS.UPDATE.COMPLETE_UPDATED' | translate: { count: r.updated } }}</p>
          @if (r.skippedNoChanges > 0) {
            <p class="update-complete__muted">{{ 'IMPORTS.UPDATE.COMPLETE_NO_CHANGES' | translate: { count: r.skippedNoChanges } }}</p>
          }
          @if (r.skippedErrors > 0) {
            <p class="update-complete__error">{{ 'IMPORTS.UPDATE.COMPLETE_ERRORS' | translate: { count: r.skippedErrors } }}</p>
          }
        </div>
      }
      <ws-button variant="primary" (click)="done.emit()">{{ 'IMPORTS.UPDATE.BACK_TO_TRANSACTIONS' | translate }}</ws-button>
    </div>
  `,
  styles: [`
    .update-complete { display: flex; flex-direction: column; align-items: center; gap: var(--space-4); padding: var(--space-8) 0; text-align: center; }
    .update-complete__icon { width: 56px; height: 56px; border-radius: var(--radius-full); background: var(--color-success-bg); color: var(--color-success); font-size: 28px; display: flex; align-items: center; justify-content: center; }
    .update-complete__title { font-size: var(--font-size-18); font-weight: 700; color: var(--color-text-primary); margin: 0; }
    .update-complete__stats { font-size: var(--font-size-14); color: var(--color-text-secondary); }
    .update-complete__stats p { margin: 0 0 4px; }
    .update-complete__muted { color: var(--color-text-tertiary); font-style: italic; }
    .update-complete__error { color: var(--color-danger); }
  `],
})
export class TxUpdateCompleteStepComponent {
  readonly result = input<TransactionUpdateResult | null>(null);
  readonly done = output<void>();
}
