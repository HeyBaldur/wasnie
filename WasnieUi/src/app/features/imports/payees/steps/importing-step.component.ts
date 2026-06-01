import { Component, OnInit, inject, input, output } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { ImportProgressComponent } from '../../shared/import-progress.component';
import { PayeeImportService } from '../services/payee-import.service';
import { PayeeImportColumnMapping, PayeeImportResult } from '../models/payee-import.models';
import { extractApiError } from '../../../../shared/utils/api-error';
import { signal } from '@angular/core';

@Component({
  selector: 'app-payee-importing-step',
  standalone: true,
  imports: [TranslateModule, ImportProgressComponent],
  templateUrl: './importing-step.component.html',
})
export class PayeeImportingStepComponent implements OnInit {
  private readonly importService = inject(PayeeImportService);

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
      this.errorMessage.set(extractApiError(err));
    }
  }

  onRetry(): void {
    this.retryRequested.emit();
  }
}
