import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent, WsSelectComponent, SelectOption } from '../../../../shared/ui';
import { PayeeImportService } from '../services/payee-import.service';
import {
  PayeeImportColumnMapping,
  ValidateResponse,
  ParseResponse,
} from '../models/payee-import.models';
import { extractApiError } from '../../../../shared/utils/api-error';
import { detectFullNameColumns, detectOtherField, OTHER_FIELD_PATTERNS } from '../helpers/column-auto-detect';
import { composeFullName } from '../helpers/fullname-composer';

@Component({
  selector: 'app-mapping-step',
  standalone: true,
  imports: [TranslateModule, ReactiveFormsModule, FormsModule, IconComponent, WsButtonComponent, WsSelectComponent],
  templateUrl: './mapping-step.component.html',
  styleUrl: './mapping-step.component.scss',
})
export class MappingStepComponent implements OnInit {
  private readonly importService = inject(PayeeImportService);
  private readonly fb = inject(FormBuilder);

  readonly parseResult = input.required<ParseResponse & { fileName: string; fileSize: number }>();
  readonly initialMapping = input<PayeeImportColumnMapping | null>(null);

  readonly validated = output<{ response: ValidateResponse; mapping: PayeeImportColumnMapping }>();
  readonly back = output<void>();
  readonly cancel = output<void>();

  readonly form = this.fb.group({
    employeeCodeColumn: [''],
    emailColumn: [''],
    hireDateColumn: [''],
    roleColumn: [''],
    managerColumn: [''],
    employmentTypeColumn: [''],
    locationColumn: [''],
  });

  readonly fullNameColumns = signal<string[]>([]);
  addColumnPickerValue = '';

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly columnOptions = computed<SelectOption[]>(() => {
    const blank: SelectOption = { value: '', label: '— Select column —' };
    return [blank, ...this.parseResult().headers.map(h => ({ value: h, label: h }))];
  });

  readonly availableHeadersForName = computed<SelectOption[]>(() => {
    const used = new Set(this.fullNameColumns());
    const blank: SelectOption = { value: '', label: '— Add column —' };
    const opts = this.parseResult().headers
      .filter(h => !used.has(h))
      .map(h => ({ value: h, label: h }));
    return opts.length > 0 ? [blank, ...opts] : [];
  });

  readonly composePreview = computed(() => {
    const rows = this.parseResult().sampleRows;
    if (rows.length === 0) return '';
    return composeFullName(rows[0], this.fullNameColumns());
  });

  get canContinue(): boolean {
    const v = this.form.value;
    return (
      this.fullNameColumns().length > 0 &&
      !!(v.employeeCodeColumn && v.emailColumn && v.hireDateColumn)
    );
  }

  get previewRows(): Record<string, string>[] {
    return this.parseResult().sampleRows.slice(0, 3);
  }

  get mappedPreviewHeaders(): { label: string; cols: string[] }[] {
    const v = this.form.value;
    return [
      { label: 'Full Name', cols: this.fullNameColumns() },
      { label: 'Employee Code', cols: [v.employeeCodeColumn ?? ''].filter(Boolean) },
      { label: 'Email', cols: [v.emailColumn ?? ''].filter(Boolean) },
      { label: 'Hire Date', cols: [v.hireDateColumn ?? ''].filter(Boolean) },
      ...(v.roleColumn ? [{ label: 'Role', cols: [v.roleColumn] }] : []),
      ...(v.managerColumn ? [{ label: 'Manager', cols: [v.managerColumn] }] : []),
      ...(v.employmentTypeColumn ? [{ label: 'Employment Type', cols: [v.employmentTypeColumn] }] : []),
      ...(v.locationColumn ? [{ label: 'Location', cols: [v.locationColumn] }] : []),
    ].filter(h => h.cols.length > 0 && h.cols.some(c => c.length > 0));
  }

  getCellValue(row: Record<string, string>, cols: string[]): string {
    return composeFullName(row, cols) || '—';
  }

  addNameColumn(col: string): void {
    if (!col) return;
    this.fullNameColumns.update(cols => (cols.includes(col) ? cols : [...cols, col]));
    this.addColumnPickerValue = '';
  }

  removeNameColumn(idx: number): void {
    this.fullNameColumns.update(cols => cols.filter((_, i) => i !== idx));
  }

  ngOnInit(): void {
    const headers = this.parseResult().headers;
    const restored = this.initialMapping();

    if (restored) {
      this.fullNameColumns.set(restored.fullNameColumns ?? []);
      this.form.patchValue({
        employeeCodeColumn: restored.employeeCodeColumn,
        emailColumn: restored.emailColumn,
        hireDateColumn: restored.hireDateColumn,
        roleColumn: restored.roleColumn ?? '',
        managerColumn: restored.managerEmployeeCodeColumn ?? '',
        employmentTypeColumn: restored.employmentTypeColumn ?? '',
        locationColumn: restored.locationColumn ?? '',
      });
      return;
    }

    this.fullNameColumns.set(detectFullNameColumns(headers));

    this.form.patchValue({
      employeeCodeColumn: detectOtherField(headers, OTHER_FIELD_PATTERNS['employeeCodeColumn']),
      emailColumn: detectOtherField(headers, OTHER_FIELD_PATTERNS['emailColumn']),
      hireDateColumn: detectOtherField(headers, OTHER_FIELD_PATTERNS['hireDateColumn']),
      roleColumn: detectOtherField(headers, OTHER_FIELD_PATTERNS['roleColumn']),
      managerColumn: detectOtherField(headers, OTHER_FIELD_PATTERNS['managerColumn']),
      employmentTypeColumn: detectOtherField(headers, OTHER_FIELD_PATTERNS['employmentTypeColumn']),
      locationColumn: detectOtherField(headers, OTHER_FIELD_PATTERNS['locationColumn']),
    });
  }

  private currentMapping(): PayeeImportColumnMapping {
    const v = this.form.value;
    return {
      fullNameColumns: this.fullNameColumns(),
      employeeCodeColumn: v.employeeCodeColumn ?? '',
      emailColumn: v.emailColumn ?? '',
      hireDateColumn: v.hireDateColumn ?? '',
      roleColumn: v.roleColumn || null,
      managerEmployeeCodeColumn: v.managerColumn || null,
      employmentTypeColumn: v.employmentTypeColumn || null,
      locationColumn: v.locationColumn || null,
    };
  }

  async onContinue(): Promise<void> {
    if (!this.canContinue) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      const mapping = this.currentMapping();
      const resp = await firstValueFrom(
        this.importService.validateMapping(this.parseResult().fileId, mapping),
      );
      this.validated.emit({ response: resp, mapping });
    } catch (err) {
      this.error.set(extractApiError(err));
    } finally {
      this.loading.set(false);
    }
  }

  onBack(): void { this.back.emit(); }

  onCancel(): void { this.cancel.emit(); }
}
