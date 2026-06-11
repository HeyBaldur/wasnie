import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { ImportProgressComponent } from '../../shared/import-progress.component';
import { PayeeImportService } from '../services/payee-import.service';
import { PayeeImportColumnMapping, PayeeImportResult } from '../models/payee-import.models';
import { extractApiError } from '../../../../shared/utils/api-error';
import { TierLimitModalService } from '../../../../shared/components/tier-limit-modal/tier-limit-modal.service';

@Component({
  selector: 'app-payee-importing-step',
  standalone: true,
  imports: [TranslateModule, ImportProgressComponent],
  templateUrl: './importing-step.component.html',
})
export class PayeeImportingStepComponent implements OnInit {
  private readonly importService = inject(PayeeImportService);
  private readonly tierLimitModal = inject(TierLimitModalService);

  readonly fileId = input.required<string>();
  readonly columnMapping = input.required<PayeeImportColumnMapping>();
  readonly skipWarnings = input.required<boolean>();

  readonly completed = output<PayeeImportResult>();
  readonly retryRequested = output<void>();

  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.runImport();
  }

  private async runImport(): Promise<void> {
    this.errorMessage.set(null);
    try {
      const result = await firstValueFrom(
        this.importService.executeImport(this.fileId(), this.columnMapping(), this.skipWarnings())
      );
      this.completed.emit(result);
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.status === 409 && err.error?.blocked === true) {
        const body = err.error;
        this.tierLimitModal.show({
          tier: body.tier ?? '',
          currentCount: body.current ?? 0,
          limit: body.limit ?? 0,
          entityKey: 'payees',
          incomingCount: body.incoming ?? 0,
        });
        this.retryRequested.emit();
        return;
      }
      this.errorMessage.set(extractApiError(err));
    }
  }

  onRetry(): void {
    this.retryRequested.emit();
  }
}
