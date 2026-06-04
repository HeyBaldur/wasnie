import { Component, computed, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent, WsBadgeComponent, WsStatCardComponent, BadgeVariant } from '../../../../shared/ui';
import { TransactionUpdateService } from '../services/transaction-update.service';
import {
  TransactionUpdateColumnMapping,
  TransactionUpdateValidateResponse,
  TransactionUpdateRowPreviewResult,
} from '../models/transaction-update.models';
import { extractApiError } from '../../../../shared/utils/api-error';

type UpdateRowFilter = 'all' | 'willUpdate' | 'noChanges' | 'errors';

@Component({
  selector: 'app-tx-update-preview-step',
  standalone: true,
  imports: [TranslateModule, IconComponent, WsButtonComponent, WsBadgeComponent, WsStatCardComponent],
  templateUrl: './update-preview-step.component.html',
  styleUrl: './update-preview-step.component.scss',
})
export class TxUpdatePreviewStepComponent {
  private readonly updateService = inject(TransactionUpdateService);

  readonly fileId = input.required<string>();
  readonly columnMapping = input.required<TransactionUpdateColumnMapping>();
  readonly validateResponse = input.required<TransactionUpdateValidateResponse>();

  readonly executed = output<string>();
  readonly back = output<void>();

  readonly rowFilter = signal<UpdateRowFilter>('all');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly filteredRows = computed(() => {
    const rows = this.validateResponse().rowResults;
    const tab = this.rowFilter();
    if (tab === 'all') return rows;
    if (tab === 'willUpdate') return rows.filter(r => r.status === 'WillUpdate');
    if (tab === 'noChanges') return rows.filter(r => r.status === 'NoChanges');
    if (tab === 'errors') return rows.filter(r => r.status === 'Error');
    return rows;
  });

  rowBadgeVariant(row: TransactionUpdateRowPreviewResult): BadgeVariant {
    switch (row.status) {
      case 'WillUpdate': return 'brand';
      case 'Error':      return 'danger';
      case 'Warning':    return 'warning';
      default:           return 'neutral';
    }
  }

  rowBadgeKey(row: TransactionUpdateRowPreviewResult): string {
    switch (row.status) {
      case 'WillUpdate': return 'IMPORTS.UPDATE.ROW_WILL_UPDATE';
      case 'Error':      return 'IMPORTS.UPDATE.ROW_ERROR';
      case 'Warning':    return 'IMPORTS.UPDATE.ROW_WARNING';
      default:           return 'IMPORTS.UPDATE.ROW_NO_CHANGES';
    }
  }

  issueBadgeVariant(issue: { severity: string; category: string }): BadgeVariant {
    if (issue.severity === 'Warning') return 'warning';
    switch (issue.category) {
      case 'Reference': return 'warning';
      case 'Format':    return 'danger';
      case 'Required':  return 'info';
      default:          return 'neutral';
    }
  }

  issueCategoryKey(issue: { severity: string; category: string }): string {
    if (issue.severity === 'Warning') return '';
    return `IMPORTS.ISSUE_CATEGORY_${issue.category.toUpperCase()}`;
  }

  getReferenceNumber(row: TransactionUpdateRowPreviewResult): string {
    const col = this.columnMapping().referenceNumberColumn;
    return row.originalData[col] ?? '';
  }

  getPayeeCode(row: TransactionUpdateRowPreviewResult): string {
    const col = this.columnMapping().payeeCodeColumn;
    return col ? (row.originalData[col] ?? '') : '';
  }

  async onExecute(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const result = await firstValueFrom(
        this.updateService.executeUpdate(this.fileId(), this.columnMapping()),
      );
      this.executed.emit(result.jobId);
    } catch (err) {
      this.error.set(extractApiError(err));
    } finally {
      this.loading.set(false);
    }
  }

  onBack(): void { this.back.emit(); }
}
