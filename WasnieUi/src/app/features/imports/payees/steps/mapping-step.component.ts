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

const SINGLE_NAME_PATTERNS = [
  'full name', 'fullname', 'full_name', 'name', 'nombre completo',
  'nome completo', 'nom complet', 'vollständiger name',
  'imię i nazwisko', 'imie i nazwisko', 'nome e cognome',
];

const FIRST_NAME_PATTERNS = [
  'first name', 'firstname', 'first_name', 'given name', 'givenname', 'first',
  'nombre', 'primer nombre', 'prénom', 'prenom', 'vorname',
  'nome', 'primeiro nome', 'imię', 'imie',
];

const LAST_NAME_PATTERNS = [
  'last name', 'lastname', 'last_name', 'surname', 'family name', 'familyname', 'last',
  'apellido', 'apellido 1', 'apellido paterno', 'primer apellido', 'apellidos',
  'nom', 'nachname', 'familienname', 'cognome', 'apelido', 'sobrenome', 'nazwisko',
];

const LAST_NAME2_PATTERNS = [
  'apellido 2', 'segundo apellido', 'apellido materno', 'segundo apelido',
  'middle name', 'middlename', 'middle_name', 'segundo nome',
  'deuxième nom', 'deuxieme nom', 'zweiter nachname',
];

const OTHER_FIELD_PATTERNS: Record<string, string[]> = {
  employeeCodeColumn: ['employee code', 'employeecode', 'employee_code', 'emp code', 'code', 'codigo', 'id', 'employee id', 'kod pracownika'],
  emailColumn: ['email', 'e-mail', 'correo', 'correo electrónico', 'correo electronico', 'e mail', 'poczta', 'adres email'],
  hireDateColumn: ['hire date', 'hiredate', 'hire_date', 'fecha ingreso', 'fecha de contratación', 'start date', 'data zatrudnienia'],
  roleColumn: ['role', 'position', 'cargo', 'puesto', 'stanowisko', 'rola'],
  managerColumn: ['manager employee code', 'manager code', 'manager id', 'manager', 'supervisor', 'jefe', 'przełożony', 'kod menedżera'],
};

function detectFullNameColumns(headers: string[]): string[] {
  const lower = headers.map(h => h.toLowerCase().trim());

  for (let i = 0; i < headers.length; i++) {
    if (SINGLE_NAME_PATTERNS.some(p => lower[i] === p)) return [headers[i]];
  }
  for (let i = 0; i < headers.length; i++) {
    if (SINGLE_NAME_PATTERNS.some(p => lower[i].includes(p))) return [headers[i]];
  }

  let first: string | null = null;
  let last1: string | null = null;
  let last2: string | null = null;

  for (let i = 0; i < headers.length; i++) {
    if (!first && FIRST_NAME_PATTERNS.some(p => lower[i] === p || lower[i].includes(p))) first = headers[i];
    if (!last1 && LAST_NAME_PATTERNS.some(p => lower[i] === p || lower[i].includes(p))) last1 = headers[i];
    if (!last2 && LAST_NAME2_PATTERNS.some(p => lower[i] === p || lower[i].includes(p))) last2 = headers[i];
  }

  if (first && last1) {
    return last2 ? [first, last1, last2] : [first, last1];
  }
  return [];
}

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

  readonly form = this.fb.group({
    employeeCodeColumn: [''],
    emailColumn: [''],
    hireDateColumn: [''],
    roleColumn: [''],
    managerColumn: [''],
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
    const cols = this.fullNameColumns();
    return cols.map(c => rows[0][c] || '').filter(Boolean).join(' ');
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
    ].filter(h => h.cols.length > 0 && h.cols.some(c => c.length > 0));
  }

  getCellValue(row: Record<string, string>, cols: string[]): string {
    return cols.map(c => row[c] || '').filter(Boolean).join(' ') || '—';
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
      });
      return;
    }

    this.fullNameColumns.set(detectFullNameColumns(headers));

    const detect = (patterns: string[]): string => {
      for (const h of headers) {
        if (patterns.some(p => h.toLowerCase().trim() === p)) return h;
      }
      for (const h of headers) {
        if (patterns.some(p => h.toLowerCase().includes(p))) return h;
      }
      return '';
    };

    this.form.patchValue({
      employeeCodeColumn: detect(OTHER_FIELD_PATTERNS['employeeCodeColumn']),
      emailColumn: detect(OTHER_FIELD_PATTERNS['emailColumn']),
      hireDateColumn: detect(OTHER_FIELD_PATTERNS['hireDateColumn']),
      roleColumn: detect(OTHER_FIELD_PATTERNS['roleColumn']),
      managerColumn: detect(OTHER_FIELD_PATTERNS['managerColumn']),
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
}
