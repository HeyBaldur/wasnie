import { Component, computed, HostListener, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent, WsStatCardComponent, WsBadgeComponent, BadgeVariant } from '../../../../shared/ui';
import { TransactionImportService } from '../services/transaction-import.service';
import {
  TransactionImportColumnMapping,
  TransactionRowValidationResult,
  TransactionValidateResponse,
  ValidationIssue,
} from '../models/transaction-import.models';
import { extractApiError } from '../../../../shared/utils/api-error';

type RowFilter = 'all' | 'errors' | 'warnings';

@Component({
  selector: 'app-tx-preview-step',
  standalone: true,
  imports: [TranslateModule, FormsModule, IconComponent, WsButtonComponent, WsStatCardComponent, WsBadgeComponent],
  templateUrl: './preview-step.component.html',
  styleUrl: './preview-step.component.scss',
})
export class TxPreviewStepComponent {
  private readonly importService = inject(TransactionImportService);

  readonly fileId = input.required<string>();
  readonly columnMapping = input.required<TransactionImportColumnMapping>();
  readonly validateResponse = input.required<TransactionValidateResponse>();

  readonly executed = output<string>();
  readonly back = output<void>();
  readonly cancel = output<void>();

  readonly rowFilter = signal<RowFilter>('all');
  skipWarnings = false;
  // Consent gate: the Import button stays disabled until the user accepts.
  // Local to this step, so it resets whenever the preview step is left/re-entered.
  consentAccepted = false;
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  // Column filter state
  readonly openFilterCol = signal<string | null>(null);
  readonly filterDropdownPos = signal<{ top: number; left: number } | null>(null);
  readonly colFilterStatus = signal<Set<string>>(new Set(['error', 'warning', 'valid']));
  readonly colFilterReference = signal('');
  readonly colFilterPayee = signal('');

  readonly statusFilterOptions: { value: string; label: string; variant: BadgeVariant }[] = [
    { value: 'error',   label: 'IMPORTS.TRANSACTIONS.ROW_ERROR',   variant: 'danger' },
    { value: 'warning', label: 'IMPORTS.TRANSACTIONS.ROW_WARNING', variant: 'warning' },
    { value: 'valid',   label: 'IMPORTS.TRANSACTIONS.ROW_VALID',   variant: 'success' },
  ];

  readonly filteredRows = computed(() => {
    const rows = this.validateResponse().rowResults;
    const tab = this.rowFilter();
    const refFilter = this.colFilterReference().toLowerCase().trim();
    const payeeFilter = this.colFilterPayee().toLowerCase().trim();
    const statusSet = this.colFilterStatus();

    return rows.filter(row => {
      if (tab === 'errors' && !row.hasErrors) return false;
      if (tab === 'warnings' && (row.hasErrors || !row.hasWarnings)) return false;

      const statusKey = row.hasErrors ? 'error' : row.hasWarnings ? 'warning' : 'valid';
      if (!statusSet.has(statusKey)) return false;

      const mapping = this.columnMapping();
      if (refFilter && !this.getCell(row, mapping.referenceNumberColumn).toLowerCase().includes(refFilter)) return false;
      if (payeeFilter && !this.getCell(row, mapping.payeeCodeColumn).toLowerCase().includes(payeeFilter)) return false;

      return true;
    });
  });

  get willImportCount(): number {
    const vr = this.validateResponse();
    if (this.skipWarnings) return vr.validRowCount - vr.warningCount;
    return vr.validRowCount;
  }

  rowBadgeVariant(row: TransactionRowValidationResult): BadgeVariant {
    if (row.hasErrors) return 'danger';
    if (row.hasWarnings) return 'warning';
    return 'success';
  }

  rowBadgeKey(row: TransactionRowValidationResult): string {
    if (row.hasErrors) return 'IMPORTS.TRANSACTIONS.ROW_ERROR';
    if (row.hasWarnings) return 'IMPORTS.TRANSACTIONS.ROW_WARNING';
    return 'IMPORTS.TRANSACTIONS.ROW_VALID';
  }

  getCell(row: TransactionRowValidationResult, colKey: string): string {
    if (!colKey) return '';
    return row.originalData[colKey] ?? '';
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

  async onImport(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const result = await firstValueFrom(
        this.importService.executeImport(this.fileId(), this.columnMapping(), this.skipWarnings),
      );
      this.executed.emit(result.jobId);
    } catch (err) {
      this.error.set(extractApiError(err));
    } finally {
      this.loading.set(false);
    }
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

  onCancel(): void { this.cancel.emit(); }
}
