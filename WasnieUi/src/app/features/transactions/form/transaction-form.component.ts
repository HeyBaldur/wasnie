import { Component, DestroyRef, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, combineLatest, map, of, startWith } from 'rxjs';
import { catchError, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { TransactionsStore } from '../state/transactions.store';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { TransactionsApiService } from '../services/transactions.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { Transaction, PlanOption, PlanOptions } from '../models/transaction.model';
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
  private readonly transactionsApi = inject(TransactionsApiService);
  private readonly destroyRef = inject(DestroyRef);
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
    // What was sold. Optional — description says which sale, these say which product.
    productName: ['', [Validators.maxLength(500)]],
    productSku: ['', [Validators.maxLength(500)]],
    transactionDate: ['', Validators.required],
    amount: [0 as number, [Validators.required, Validators.min(0.01)]],
    currency: ['USD', Validators.required],
    quantity: [1 as number, [Validators.required, Validators.min(1)]],
    // Which plan this sale is credited to. Only required when the payee is on 2+ applicable plans —
    // see planSelectionRequired. The backend re-validates, this is not the only gate.
    selectedPlanAssignmentId: [''],
    processImmediately: [true],
  });

  // ── Plan attribution ────────────────────────────────────────────────────────────────────
  // A payee on several applicable plans used to have the plan chosen by an arbitrary engine
  // tie-break, silently deciding how much commission was paid. The admin now states it explicitly.
  // The candidate list depends on payee AND date AND currency, so it is reloaded when any change.
  readonly planOptions = signal<PlanOption[]>([]);
  readonly planSelectionRequired = signal(false);
  readonly planOptionsLoading = signal(false);

  readonly planSelectOptions = computed<SelectOption[]>(() =>
    this.planOptions().map(o => ({ value: o.planAssignmentId, label: o.planName }))
  );

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

    // Reload the candidate plans whenever an input that changes eligibility changes.
    combineLatest([
      this.form.controls.payeeId.valueChanges.pipe(startWith(this.form.controls.payeeId.value)),
      this.form.controls.transactionDate.valueChanges.pipe(startWith(this.form.controls.transactionDate.value)),
      this.form.controls.currency.valueChanges.pipe(startWith(this.form.controls.currency.value)),
    ])
      .pipe(
        distinctUntilChanged((a, b) => a[0] === b[0] && a[1] === b[1] && a[2] === b[2]),
        // switchMap so a slower earlier response can never overwrite a newer selection.
        switchMap(([payeeId, transactionDate, currency]) => {
          if (!payeeId || !transactionDate || !currency) return of(null);
          this.planOptionsLoading.set(true);
          return this.transactionsApi.getPlanOptions(payeeId, transactionDate, currency).pipe(
            catchError(() => of(null)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(result => {
        this.planOptionsLoading.set(false);
        this.applyPlanOptions(result);
      });
  }

  /**
   * Applies a freshly loaded candidate set. On failure or an incomplete form the selector is emptied
   * and left NOT required — the server still rejects an ambiguous ingest, so a failed lookup degrades
   * to "backend refuses with a clear message" rather than to "silently attributed by tie-break".
   */
  private applyPlanOptions(result: PlanOptions | null): void {
    const options = result?.options ?? [];
    const required = result?.selectionRequired ?? false;
    const ctrl = this.form.controls.selectedPlanAssignmentId;

    this.planOptions.set(options);
    this.planSelectionRequired.set(required);

    // Drop a stale choice that is no longer among the candidates (e.g. the date moved).
    if (ctrl.value && !options.some(o => o.planAssignmentId === ctrl.value)) {
      ctrl.setValue('', { emitEvent: false });
    }

    if (required) {
      ctrl.addValidators(Validators.required);
    } else {
      ctrl.removeValidators(Validators.required);
      // With 0 or 1 candidate there is nothing to disambiguate; never send a value the admin
      // was not actually asked for.
      ctrl.setValue('', { emitEvent: false });
    }
    ctrl.updateValueAndValidity({ emitEvent: false });
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
        productName: v.productName.trim() || null,
        productSku: v.productSku.trim() || null,
        transactionDate: v.transactionDate,
        amount: v.amount,
        currency: v.currency,
        quantity: v.quantity,
        selectedPlanAssignmentId: v.selectedPlanAssignmentId || null,
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
