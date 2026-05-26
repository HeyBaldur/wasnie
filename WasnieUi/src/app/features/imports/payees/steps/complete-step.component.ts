import { Component, input, output } from '@angular/core';
import { Router } from '@angular/router';
import { inject } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent } from '../../../../shared/ui';
import { PayeeImportResult } from '../models/payee-import.models';

@Component({
  selector: 'app-complete-step',
  standalone: true,
  imports: [TranslateModule, IconComponent, WsButtonComponent],
  templateUrl: './complete-step.component.html',
  styleUrl: './complete-step.component.scss',
})
export class CompleteStepComponent {
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
