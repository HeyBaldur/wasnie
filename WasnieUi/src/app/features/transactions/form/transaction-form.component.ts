import { Component, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, map } from 'rxjs';
import { TransactionsStore } from '../state/transactions.store';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { Transaction } from '../models/transaction.model';
import { SettingsApiService, FieldRequirement } from '../../admin/services/settings.api.service';
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
export class TransactionFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly store = inject(TransactionsStore);
  private readonly payeesApi = inject(PayeesApiService);
  private readonly toast = inject(ToastService);
  private readonly settingsApi = inject(SettingsApiService);

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

  // Whether a payee is required is a per-tenant setting ("Require payee on new transactions").
  // The backend already gates on it in IngestTransactionHandler; this form previously hardcoded
  // Validators.required, making the UI stricter than its own API. Default false to match the
  // backend fallback (FieldRequirementService: `?.IsRequired ?? false`) and the seeded default.
  readonly fieldRequirements = signal<FieldRequirement[]>([]);

  readonly payeeRequired = computed(() =>
    this.fieldRequirements().find(
      r => r.entityName === 'Transaction' && r.fieldName === 'PayeeId'
    )?.isRequired ?? false
  );

  readonly form = this.fb.nonNullable.group({
    payeeId: [''],
    referenceNumber: ['', [Validators.required, Validators.maxLength(100)]],
    // Optional label so an auditor can tell what the transaction is for without opening the CRM.
    description: ['', [Validators.maxLength(500)]],
    transactionDate: ['', Validators.required],
    amount: [0 as number, [Validators.required, Validators.min(0.01)]],
    currency: ['USD', Validators.required],
    quantity: [1 as number, [Validators.required, Validators.min(1)]],
    processImmediately: [true],
  });

  constructor() {
    effect(() => {
      const ctrl = this.form.controls.payeeId;
      if (this.payeeRequired()) {
        ctrl.addValidators(Validators.required);
      } else {
        ctrl.removeValidators(Validators.required);
      }
      ctrl.updateValueAndValidity({ emitEvent: false });
    });
  }

  ngOnInit(): void {
    this.settingsApi.getFieldRequirements().subscribe({
      next: reqs => this.fieldRequirements.set(reqs),
      error: () => { /* keep the safe default (optional), matching the backend fallback */ },
    });
  }

  async onSubmit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    this.saving.set(true);
    try {
      const result = await this.store.createTransaction({
        // Blank means "no payee" (Unassigned), not an empty GUID — the backend validator
        // rejects Guid.Empty but accepts null when the tenant setting is Optional.
        payeeId: v.payeeId || null,
        referenceNumber: v.referenceNumber.trim(),
        description: v.description.trim() || null,
        transactionDate: v.transactionDate,
        amount: v.amount,
        currency: v.currency,
        quantity: v.quantity,
        processImmediately: v.processImmediately,
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
