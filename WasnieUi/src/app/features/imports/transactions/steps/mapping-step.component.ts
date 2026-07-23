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
import { SettingsApiService, FieldRequirement } from '../../../admin/services/settings.api.service';

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
  private readonly settingsApi = inject(SettingsApiService);

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
    descriptionColumn: [''],
  });

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  // Whether a payee is required comes from the per-tenant setting (Settings → Field
  // requirements → "Require payee on new transactions"), the same source the backend uses
  // in TransactionImportValidationService and IngestTransactionHandler.
  //
  // Default false, matching the backend's own fallback (FieldRequirementService returns
  // `?.IsRequired ?? false`) and the seeded tenant default. Unlike the payee fields, the
  // safe default here is Optional: a blank payee is a supported outcome (Unassigned), so
  // falling back to required would block an import the backend would have accepted.
  readonly fieldRequirements = signal<FieldRequirement[]>([]);

  readonly payeeRequired = computed(() =>
    this.fieldRequirements().find(
      r => r.entityName === 'Transaction' && r.fieldName === 'PayeeId'
    )?.isRequired ?? false
  );

  readonly columnOptions = computed<SelectOption[]>(() => {
    const blank: SelectOption = { value: '', label: '— Select column —' };
    return [blank, ...this.parseResult().headers.map(h => ({ value: h, label: h }))];
  });

  get canContinue(): boolean {
    const v = this.form.value;
    return !!(
      v.referenceNumberColumn &&
      v.amountColumn &&
      v.currencyColumn &&
      v.transactionDateColumn
    ) && (!this.payeeRequired() || !!v.payeeCodeColumn);
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
      ...(v.descriptionColumn ? [{ label: 'Description', col: v.descriptionColumn }] : []),
    ].filter(h => h.col.length > 0);
  }

  getCellValue(row: Record<string, string>, col: string): string {
    return row[col] ?? '—';
  }

  ngOnInit(): void {
    this.settingsApi.getFieldRequirements().subscribe({
      next: reqs => this.fieldRequirements.set(reqs),
      error: () => { /* keep the safe default (optional), matching the backend fallback */ },
    });

    const headers = this.parseResult().headers;
    const restored = this.initialMapping();

    if (restored) {
      this.form.patchValue({
        referenceNumberColumn: restored.referenceNumberColumn,
        payeeCodeColumn: restored.payeeCodeColumn ?? '',
        amountColumn: restored.amountColumn,
        currencyColumn: restored.currencyColumn,
        transactionDateColumn: restored.transactionDateColumn,
        externalIdColumn: restored.externalIdColumn ?? '',
        descriptionColumn: restored.descriptionColumn ?? '',
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
      descriptionColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['descriptionColumn']),
    });
  }

  private currentMapping(): TransactionImportColumnMapping {
    const v = this.form.value;
    return {
      referenceNumberColumn: v.referenceNumberColumn ?? '',
      payeeCodeColumn: v.payeeCodeColumn || null,
      amountColumn: v.amountColumn ?? '',
      currencyColumn: v.currencyColumn ?? '',
      transactionDateColumn: v.transactionDateColumn ?? '',
      externalIdColumn: v.externalIdColumn || null,
      descriptionColumn: v.descriptionColumn || null,
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
