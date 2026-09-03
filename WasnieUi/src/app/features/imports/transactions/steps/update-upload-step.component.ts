import { Component, inject, output, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent } from '../../../../shared/ui';
import { ImportDropzoneComponent } from '../../shared/import-dropzone.component';
import { TransactionUpdateService } from '../services/transaction-update.service';
import { ParseResponse } from '../models/transaction-import.models';
import { extractApiError } from '../../../../shared/utils/api-error';

const MAX_BYTES = 5 * 1024 * 1024;

@Component({
  selector: 'app-tx-update-upload-step',
  standalone: true,
  imports: [TranslateModule, DecimalPipe, IconComponent, WsButtonComponent, ImportDropzoneComponent],
  templateUrl: './update-upload-step.component.html',
  styleUrl: './upload-step.component.scss',
})
export class TxUpdateUploadStepComponent {
  private readonly updateService = inject(TransactionUpdateService);

  readonly parsed = output<ParseResponse & { fileName: string; fileSize: number }>();

  readonly selectedFile = signal<File | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly maxRows = signal<number | null>(null); // update mode: no row limit shown

  selectFile(file: File): void {
    this.error.set(null);
    const ext = file.name.split('.').pop()?.toLowerCase() ?? '';
    if (!['csv', 'xlsx'].includes(ext)) {
      this.error.set('IMPORTS.TRANSACTIONS.ERROR_INVALID_FORMAT');
      return;
    }
    if (file.size > MAX_BYTES) {
      this.error.set('IMPORTS.TRANSACTIONS.ERROR_FILE_TOO_LARGE');
      return;
    }
    this.selectedFile.set(file);
  }

  async onParse(): Promise<void> {
    const file = this.selectedFile();
    if (!file) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      const resp = await firstValueFrom(this.updateService.parseFile(file));
      this.parsed.emit({ ...resp, fileName: file.name, fileSize: file.size });
    } catch (err) {
      this.error.set(extractApiError(err));
    } finally {
      this.loading.set(false);
    }
  }

  // No sample download for update mode — the user should upload their own exported file.
  downloadSample(): void { /* no-op for update mode */ }

  clearFile(): void {
    this.selectedFile.set(null);
    this.error.set(null);
  }
}
