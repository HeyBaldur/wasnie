import { Component, inject, OnInit, output, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent } from '../../../../shared/ui';
import { TransactionImportService } from '../services/transaction-import.service';
import { ParseResponse } from '../models/transaction-import.models';
import { extractApiError } from '../../../../shared/utils/api-error';

const MAX_BYTES = 5 * 1024 * 1024;
const SAMPLE_HEADERS = ['Reference Number', 'Payee Code', 'Amount', 'Currency', 'Transaction Date', 'External ID'];

@Component({
  selector: 'app-tx-upload-step',
  standalone: true,
  imports: [TranslateModule, DecimalPipe, IconComponent, WsButtonComponent],
  templateUrl: './upload-step.component.html',
  styleUrl: './upload-step.component.scss',
})
export class TxUploadStepComponent implements OnInit {
  private readonly importService = inject(TransactionImportService);

  readonly parsed = output<ParseResponse & { fileName: string; fileSize: number }>();

  readonly isDragging = signal(false);
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

  onDragOver(e: DragEvent): void {
    e.preventDefault();
    this.isDragging.set(true);
  }

  onDragLeave(): void {
    this.isDragging.set(false);
  }

  onDrop(e: DragEvent): void {
    e.preventDefault();
    this.isDragging.set(false);
    const file = e.dataTransfer?.files[0];
    if (file) this.selectFile(file);
  }

  onFileInput(e: Event): void {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.selectFile(file);
    input.value = '';
  }

  private selectFile(file: File): void {
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

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
