import { Component, DestroyRef, input, output } from '@angular/core';
import { Router } from '@angular/router';
import { inject } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent } from '../../../../shared/ui';
import { PayeeImportResult } from '../models/payee-import.models';
import { launchConfetti } from '../../../../shared/ui/ws-confetti/confetti';

@Component({
  selector: 'app-complete-step',
  standalone: true,
  imports: [TranslateModule, IconComponent, WsButtonComponent],
  templateUrl: './complete-step.component.html',
  styleUrl: './complete-step.component.scss',
})
export class CompleteStepComponent {
  constructor() {
    // A short burst for a finished import — the moment the work lands. Skipped entirely with reduced motion
    // (launchConfetti checks), and cleared if the user leaves before it ends.
    const stop = launchConfetti({ durationMs: 2600, particleCount: 140 });
    inject(DestroyRef).onDestroy(stop);
  }

  private readonly router = inject(Router);

  readonly result = input.required<PayeeImportResult>();
  readonly importMore = output<void>();

  goToPayees(): void {
    this.router.navigate(['/payees']);
  }

  onImportMore(): void {
    this.importMore.emit();
  }
}
