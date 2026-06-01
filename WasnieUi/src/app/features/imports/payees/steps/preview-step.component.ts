import { Component, computed, HostListener, input, output, signal } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent, WsStatCardComponent, WsBadgeComponent, BadgeVariant } from '../../../../shared/ui';
import {
  PayeeImportColumnMapping,
  PayeeRowValidationResult,
  ValidateResponse,
  ValidationIssue,
} from '../models/payee-import.models';
import { composeFullName } from '../helpers/fullname-composer';

type RowFilter = 'all' | 'errors' | 'warnings';

@Component({
  selector: 'app-preview-step',
  standalone: true,
  imports: [TranslateModule, FormsModule, IconComponent, WsButtonComponent, WsStatCardComponent, WsBadgeComponent],
  templateUrl: './preview-step.component.html',
  styleUrl: './preview-step.component.scss',
})
export class PreviewStepComponent {
  readonly fileId = input.required<string>();
  readonly columnMapping = input.required<PayeeImportColumnMapping>();
  readonly validateResponse = input.required<ValidateResponse>();

  readonly importRequested = output<{ skipWarnings: boolean }>();
  readonly back = output<void>();

  readonly rowFilter = signal<RowFilter>('all');
  skipWarnings = false;

  // Column filter state
  readonly openFilterCol = signal<string | null>(null);
  readonly filterDropdownPos = signal<{ top: number; left: number } | null>(null);
  readonly colFilterStatus = signal<Set<string>>(new Set(['error', 'warning', 'valid']));
  readonly colFilterName = signal('');
  readonly colFilterEmail = signal('');

  readonly statusFilterOptions: { value: string; label: string; variant: BadgeVariant }[] = [
    { value: 'error',   label: 'IMPORTS.PAYEES.ROW_ERROR',   variant: 'danger' },
    { value: 'warning', label: 'IMPORTS.PAYEES.ROW_WARNING', variant: 'warning' },
    { value: 'valid',   label: 'IMPORTS.PAYEES.ROW_VALID',   variant: 'success' },
  ];

  readonly filteredRows = computed(() => {
    const rows = this.validateResponse().rowResults;
    const tab = this.rowFilter();
    const nameFilter = this.colFilterName().toLowerCase().trim();
    const emailFilter = this.colFilterEmail().toLowerCase().trim();
    const statusSet = this.colFilterStatus();

    return rows.filter(row => {
      if (tab === 'errors' && !row.hasErrors) return false;
      if (tab === 'warnings' && (row.hasErrors || !row.hasWarnings)) return false;

      const statusKey = row.hasErrors ? 'error' : row.hasWarnings ? 'warning' : 'valid';
      if (!statusSet.has(statusKey)) return false;

      if (nameFilter && !this.getCell(row, 'fullNameColumns').toLowerCase().includes(nameFilter)) return false;
      if (emailFilter && !this.getCell(row, 'emailColumn').toLowerCase().includes(emailFilter)) return false;

      return true;
    });
  });

  get willImportCount(): number {
    const vr = this.validateResponse();
    if (this.skipWarnings) return vr.validRowCount - vr.warningCount;
    return vr.validRowCount;
  }

  rowBadgeVariant(row: PayeeRowValidationResult): BadgeVariant {
    if (row.hasErrors) return 'danger';
    if (row.hasWarnings) return 'warning';
    return 'success';
  }

  rowBadgeKey(row: PayeeRowValidationResult): string {
    if (row.hasErrors) return 'IMPORTS.PAYEES.ROW_ERROR';
    if (row.hasWarnings) return 'IMPORTS.PAYEES.ROW_WARNING';
    return 'IMPORTS.PAYEES.ROW_VALID';
  }

  getCell(row: PayeeRowValidationResult, colKey: keyof PayeeImportColumnMapping): string {
    const mapping = this.columnMapping();
    if (colKey === 'fullNameColumns') {
      return composeFullName(row.originalData, mapping.fullNameColumns ?? []);
    }
    const col = mapping[colKey] as string | null | undefined;
    if (!col) return '';
    return row.originalData[col] ?? '';
  }

  toggleColFilter(col: string, event: Event): void {
    event.stopPropagation();
    const isOpening = this.openFilterCol() !== col;
    this.openFilterCol.update(cur => cur === col ? null : col);
    if (isOpening) {
      const btn = event.currentTarget as HTMLElement;
      const rect = btn.getBoundingClientRect();
      this.filterDropdownPos.set({ top: rect.bottom + 4, left: rect.left });
    } else {
      this.filterDropdownPos.set(null);
    }
  }

  toggleStatusFilter(value: string): void {
    const next = new Set(this.colFilterStatus());
    if (next.has(value)) { next.delete(value); } else { next.add(value); }
    this.colFilterStatus.set(next);
  }

  isStatusFilterActive(): boolean {
    return this.colFilterStatus().size < 3;
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.openFilterCol.set(null);
    this.filterDropdownPos.set(null);
  }

  onImport(): void {
    this.importRequested.emit({ skipWarnings: this.skipWarnings });
  }

  issueBadgeVariant(issue: ValidationIssue): BadgeVariant {
    if (issue.severity === 'Warning') return 'warning';
    switch (issue.category) {
      case 'Reference': return 'warning';
      case 'Format':    return 'danger';
      case 'Required':  return 'info';
      default:          return 'neutral';
    }
  }

  issueCategoryKey(issue: ValidationIssue): string {
    if (issue.severity === 'Warning') return '';
    return `IMPORTS.ISSUE_CATEGORY_${issue.category.toUpperCase()}`;
  }

  onBack(): void { this.back.emit(); }
}
