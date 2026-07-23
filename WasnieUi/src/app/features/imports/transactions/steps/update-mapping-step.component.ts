import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { WsButtonComponent, WsSelectComponent, SelectOption } from '../../../../shared/ui';
import { TransactionUpdateService } from '../services/transaction-update.service';
import {
  TransactionUpdateColumnMapping,
  TransactionUpdateValidateResponse,
} from '../models/transaction-update.models';
import { ParseResponse } from '../models/transaction-import.models';
import { extractApiError } from '../../../../shared/utils/api-error';
import { detectField, TRANSACTION_FIELD_PATTERNS } from '../helpers/column-auto-detect';

@Component({
  selector: 'app-tx-update-mapping-step',
  standalone: true,
  imports: [TranslateModule, ReactiveFormsModule, IconComponent, WsButtonComponent, WsSelectComponent],
  templateUrl: './update-mapping-step.component.html',
  styleUrl: './update-mapping-step.component.scss',
})
export class TxUpdateMappingStepComponent implements OnInit {
  private readonly updateService = inject(TransactionUpdateService);
  private readonly fb = inject(FormBuilder);

  readonly parseResult = input.required<ParseResponse & { fileName: string; fileSize: number }>();

  readonly validated = output<{ response: TransactionUpdateValidateResponse; mapping: TransactionUpdateColumnMapping }>();
  readonly back = output<void>();
  readonly cancel = output<void>();

  readonly form = this.fb.group({
    referenceNumberColumn: [''],
    amountColumn: [''],
    currencyColumn: [''],
    transactionDateColumn: [''],
    payeeCodeColumn: [''],
    quantityColumn: [''],
    descriptionColumn: [''],
  });

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly columnOptions = computed<SelectOption[]>(() =>
    this.parseResult().headers.map(h => ({ value: h, label: h }))
  );

  readonly optionalColumnOptions = computed<SelectOption[]>(() => {
    const blank: SelectOption = { value: '', label: '— Skip this field —' };
    return [blank, ...this.parseResult().headers.map(h => ({ value: h, label: h }))];
  });

  get canContinue(): boolean {
    return !!this.form.value.referenceNumberColumn;
  }

  ngOnInit(): void {
    const headers = this.parseResult().headers;
    this.form.patchValue({
      referenceNumberColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['referenceNumberColumn']) ?? '',
      amountColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['amountColumn']) ?? '',
      currencyColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['currencyColumn']) ?? '',
      transactionDateColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['transactionDateColumn']) ?? '',
      payeeCodeColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['payeeCodeColumn']) ?? '',
      quantityColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['quantityColumn']) ?? '',
      descriptionColumn: detectField(headers, TRANSACTION_FIELD_PATTERNS['descriptionColumn']) ?? '',
    });
  }

  async onValidate(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    const v = this.form.value;
    const mapping: TransactionUpdateColumnMapping = {
      referenceNumberColumn: v.referenceNumberColumn!,
      amountColumn: v.amountColumn || null,
      currencyColumn: v.currencyColumn || null,
      transactionDateColumn: v.transactionDateColumn || null,
      payeeCodeColumn: v.payeeCodeColumn || null,
      quantityColumn: v.quantityColumn || null,
      descriptionColumn: v.descriptionColumn || null,
    };
    try {
      const response = await firstValueFrom(
        this.updateService.validateMapping(this.parseResult().fileId, mapping),
      );
      this.validated.emit({ response, mapping });
    } catch (err) {
      this.error.set(extractApiError(err));
    } finally {
      this.loading.set(false);
    }
  }
}
