import { Component, input, output } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { WsButtonComponent } from '../../../shared/ui';

@Component({
  selector: 'app-import-progress',
  standalone: true,
  imports: [TranslateModule, IconComponent, WsButtonComponent],
  templateUrl: './import-progress.component.html',
  styleUrl: './import-progress.component.scss',
})
export class ImportProgressComponent {
  /** Displayed while loading and as the failure title */
  readonly title = input<string>('');
  /** Displayed below the progress bar while loading */
  readonly subtitle = input<string>('');
  /**
   * null  → indeterminate (animated sweep, used for synchronous HTTP imports)
   * 0-100 → determinate (used for polled background jobs)
   */
  readonly progress = input<number | null>(null);
  /** When set, switches to the failure view */
  readonly errorMessage = input<string | null>(null);
  /** Transient network error (retry will be attempted automatically) */
  readonly netError = input(false);

  readonly retry = output<void>();

  onRetry(): void {
    this.retry.emit();
  }
}
