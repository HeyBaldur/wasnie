import { Component, input, output } from '@angular/core';
import { Router } from '@angular/router';
import { inject } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent } from '../../../../shared/ui';
import { TransactionImportResult } from '../models/transaction-import.models';

@Component({
  selector: 'app-tx-complete-step',
  standalone: true,
  imports: [TranslateModule, IconComponent, WsButtonComponent],
  templateUrl: './complete-step.component.html',
  styleUrl: './complete-step.component.scss',
})
export class TxCompleteStepComponent {
  private readonly router = inject(Router);

  readonly result = input.required<TransactionImportResult>();
  readonly importMore = output<void>();

  get skippedCount(): number {
    const r = this.result();
    return r.totalRows - r.processedRows;
  }

  goToTransactions(): void {
    this.router.navigate(['/transactions']);
  }

  onImportMore(): void {
    this.importMore.emit();
  }
}
