import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent, WsSelectComponent, SelectOption } from '../../../../shared/ui';
import { TransactionImportService } from '../services/transaction-import.service';
import {
  TransactionImportColumnMapping,
  TransactionValidateResponse,
  ParseResponse,
} from '../models/transaction-import.models';
import { extractApiError } from '../../../../shared/utils/api-error';
import { detectField, TRANSACTION_FIELD_PATTERNS } from '../helpers/column-auto-detect';

@Component({
  selector: 'app-tx-mapping-step',
  standalone: true,
  imports: [TranslateModule, ReactiveFormsModule, FormsModule, IconComponent, WsButtonComponent, WsSelectComponent],
  templateUrl: './mapping-step.component.html',
  styleUrl: './mapping-step.component.scss',
})
export class TxMappingStepComponent implements OnInit {
  private readonly importService = inject(TransactionImportService);
  private readonly fb = inject(FormBuilder);

  readonly parseResult = input.required<ParseResponse & { fileName: string; fileSize: number }>();
  readonly initialMapping = input<TransactionImportColumnMapping | null>(null);

  readonly validated = output<{ response: TransactionValidateResponse; mapping: TransactionImportColumnMapping }>();
  readonly back = output<void>();
  readonly cancel = output<void>();

  readonly form = this.fb.group({
    referenceNumberColumn: [''],
    amountColumn: [''],
    currencyColumn: [''],
    transactionDateColumn: [''],
    payeeCodeColumn: [''],
    externalIdColumn: [''],
  });

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly columnOptions = computed<SelectOption[]>(() => {
    const blank: SelectOption = { value: '', label: '— Select column —' };
    return [blank, ...this.parseResult().headers.map(h => ({ value: h, label: h }))];
  });

  get canContinue(): boolean {
    const v = this.form.value;
    return !!(
      v.referenceNumberColumn &&
      v.payeeCodeColumn &&
      v.amountColumn &&
      v.currencyColumn &&
      v.transactionDateColumn
    );
  }

  get previewRows(): Record<string, string>[] {
    return this.parseResult().sampleRows.slice(0, 3);
  }

  get mappedPreviewHeaders(): { label: string; col: string }[] {
    const v = this.form.value;
    return [
      { label: 'Reference Number', col: v.referenceNumberColumn ?? '' },
      { label: 'Payee Code', col: v.payeeCodeColumn ?? '' },
      { label: 'Amount', col: v.amountColumn ?? '' },
      { label: 'Currency', col: v.currencyColumn ?? '' },
      { label: 'Transaction Date', col: v.transactionDateColumn ?? '' },
      ...(v.externalIdColumn ? [{ label: 'External ID', col: v.externalIdColumn }] : []),
    ].filter(h => h.col.length > 0);
  }

  getCellValue(row: Record<string, string>, col: string): string {
    return row[col] ?? '—';
  }

  ngOnInit(): void {
    const headers = this.parseResult().headers;
    const restored = this.initialMapping();

    if (restored) {
      this.form.patchValue({
        referenceNumberColumn: restored.referenceNumberColumn,
        payeeCodeColumn: restored.payeeCodeColumn,
        amountColumn: restored.amountColumn,
        currencyColumn: restored.currencyColumn,
        transactionDateColumn: restored.transactionDateColumn,
        externalIdColumn: restored.externalIdColumn ?? '',
      });
      return;
    }

    this.form.patchValue({
      referenceNumberColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['referenceNumberColumn']),
      payeeCodeColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['payeeCodeColumn']),
      amountColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['amountColumn']),
      currencyColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['currencyColumn']),
      transactionDateColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['transactionDateColumn']),
      externalIdColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['externalIdColumn']),
    });
  }

  private currentMapping(): TransactionImportColumnMapping {
    const v = this.form.value;
    return {
      referenceNumberColumn: v.referenceNumberColumn ?? '',
      payeeCodeColumn: v.payeeCodeColumn ?? '',
      amountColumn: v.amountColumn ?? '',
      currencyColumn: v.currencyColumn ?? '',
      transactionDateColumn: v.transactionDateColumn ?? '',
      externalIdColumn: v.externalIdColumn || null,
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
