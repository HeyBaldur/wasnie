import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, map } from 'rxjs';
import { TransactionsStore } from '../state/transactions.store';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { Transaction } from '../models/transaction.model';
import {
  WsButtonComponent,
  WsInputComponent,
  WsSelectComponent,
  WsDatePickerComponent,
  type SelectOption,
} from '../../../shared/ui';

const CURRENCIES: SelectOption[] = [
  'USD', 'EUR', 'GBP', 'PLN', 'CAD', 'AUD',
].map((c) => ({ value: c, label: c }));

@Component({
  selector: 'app-transaction-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TranslateModule,
    WsButtonComponent,
    WsInputComponent,
    WsSelectComponent,
    WsDatePickerComponent,
  ],
  templateUrl: './transaction-form.component.html',
  styleUrl: './transaction-form.component.scss',
})
export class TransactionFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly store = inject(TransactionsStore);
  private readonly payeesApi = inject(PayeesApiService);
  private readonly toast = inject(ToastService);

  readonly transaction = input<Transaction | null>(null);
  readonly saved = output<Transaction>();
  readonly cancelled = output<void>();

  readonly isEditMode = computed(() => this.transaction() !== null);
  readonly saving = signal(false);
  readonly currencies = CURRENCIES;

  readonly payeeSearchFn = (q: string): Observable<SelectOption[]> =>
    this.payeesApi.getPayees({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => ({ value: p.id, label: `${p.fullName} (${p.employeeCode})` })))
    );

  readonly form = this.fb.nonNullable.group({
    payeeId: ['', Validators.required],
    referenceNumber: ['', [Validators.required, Validators.maxLength(100)]],
    transactionDate: ['', Validators.required],
    amount: [0 as number, [Validators.required, Validators.min(0.01)]],
    currency: ['USD', Validators.required],
  });

  async onSubmit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    this.saving.set(true);
    try {
      const result = await this.store.createTransaction({
        payeeId: v.payeeId,
        referenceNumber: v.referenceNumber.trim(),
        transactionDate: v.transactionDate,
        amount: v.amount,
        currency: v.currency,
      });
      this.toast.show('TRANSACTIONS.TOAST_CREATED', 'success');
      this.saved.emit(result);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.saving.set(false);
    }
  }

  onCancel(): void {
    this.cancelled.emit();
  }

  hasError(field: string, error: string): boolean {
    const ctrl = this.form.get(field);
    return !!(ctrl?.touched && ctrl.hasError(error));
  }
}
