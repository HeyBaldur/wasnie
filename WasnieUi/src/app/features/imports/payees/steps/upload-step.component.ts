import { Component, inject, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent } from '../../../../shared/ui';
import { ImportDropzoneComponent } from '../../shared/import-dropzone.component';
import { PayeeImportService } from '../services/payee-import.service';
import { ParseResponse } from '../models/payee-import.models';
import { extractApiError } from '../../../../shared/utils/api-error';

const MAX_BYTES = 5 * 1024 * 1024;
const SAMPLE_HEADERS = ['Full Name', 'Employee Code', 'Email', 'Hire Date', 'Role', 'Manager Employee Code'];

@Component({
  selector: 'app-upload-step',
  standalone: true,
  imports: [TranslateModule, IconComponent, WsButtonComponent, ImportDropzoneComponent],
  templateUrl: './upload-step.component.html',
  styleUrl: './upload-step.component.scss',
})
export class UploadStepComponent {
  private readonly importService = inject(PayeeImportService);

  readonly parsed = output<ParseResponse & { fileName: string; fileSize: number }>();

  readonly selectedFile = signal<File | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  selectFile(file: File): void {
    this.error.set(null);
    const ext = file.name.split('.').pop()?.toLowerCase() ?? '';
    if (!['csv', 'xlsx'].includes(ext)) {
      this.error.set('IMPORTS.PAYEES.ERROR_INVALID_FORMAT');
      return;
    }
    if (file.size > MAX_BYTES) {
      this.error.set('IMPORTS.PAYEES.ERROR_FILE_TOO_LARGE');
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
    const csv = [SAMPLE_HEADERS.join(','), 'Jane Smith,EMP001,jane@acme.com,2024-01-15,Account Executive,MGR001'].join('\n');
    const blob = new Blob([csv], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'payees-import-sample.csv';
    a.click();
    URL.revokeObjectURL(url);
  }

  clearFile(): void {
    this.selectedFile.set(null);
    this.error.set(null);
  }
}
