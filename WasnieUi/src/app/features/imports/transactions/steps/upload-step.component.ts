import { Component, inject, OnInit, output, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent } from '../../../../shared/ui';
import { ImportDropzoneComponent } from '../../shared/import-dropzone.component';
import { TransactionImportService } from '../services/transaction-import.service';
import { ParseResponse } from '../models/transaction-import.models';
import { extractApiError } from '../../../../shared/utils/api-error';

const MAX_BYTES = 5 * 1024 * 1024;
const SAMPLE_HEADERS = ['Reference Number', 'Payee Code', 'Amount', 'Currency', 'Transaction Date', 'External ID'];

@Component({
  selector: 'app-tx-upload-step',
  standalone: true,
  imports: [TranslateModule, DecimalPipe, IconComponent, WsButtonComponent, ImportDropzoneComponent],
  templateUrl: './upload-step.component.html',
  styleUrl: './upload-step.component.scss',
})
export class TxUploadStepComponent implements OnInit {
  private readonly importService = inject(TransactionImportService);

  readonly parsed = output<ParseResponse & { fileName: string; fileSize: number }>();

  readonly selectedFile = signal<File | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly maxRows = signal(10_000);

  ngOnInit(): void {
    this.importService.getImportLimits().subscribe({
      next: limits => this.maxRows.set(limits.maxRows),
      error: () => { /* keep default 10_000 on network failure */ },
    });
  }

  /**
   * ★ VALIDATION STAYED HERE WHEN THE PICKER MOVED OUT. `app-import-dropzone` reports the file the
   * user chose and nothing else: what counts as an acceptable file — the extensions, the size cap,
   * the message shown when it is not — is this import's rule, and a picker that decided it would
   * have to be told the rules of all three wizards.
   */
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
      const resp = await firstValueFrom(this.importService.parseFile(file));
      this.parsed.emit({ ...resp, fileName: file.name, fileSize: file.size });
    } catch (err) {
      this.error.set(extractApiError(err));
    } finally {
      this.loading.set(false);
    }
  }

  downloadSample(): void {
    const csv = [SAMPLE_HEADERS.join(','), 'REF-001,EMP001,1500.00,USD,2024-01-15,EXT-001'].join('\n');
    const blob = new Blob([csv], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'transactions-import-sample.csv';
    a.click();
    URL.revokeObjectURL(url);
  }

  clearFile(): void {
    this.selectedFile.set(null);
    this.error.set(null);
  }
}
