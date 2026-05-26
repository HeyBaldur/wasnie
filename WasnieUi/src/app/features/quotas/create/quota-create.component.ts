import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { QuotasStore } from '../state/quotas.store';
import { PayeesStore } from '../../payees/state/payees.store';
import { PlansStore } from '../../plans/state/plans.store';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { PayeeStatus } from '../../payees/models/payee.model';
import { QuotaMeasurementType } from '../models/quota.model';
import {
  WsButtonComponent,
  WsInputComponent,
  WsSelectComponent,
  WsDateRangePickerComponent,
  WsPageHeaderComponent,
  type SelectOption,
  type DateRange,
} from '../../../shared/ui';

const CURRENCIES: SelectOption[] = [
  'USD', 'EUR', 'GBP', 'PLN', 'CAD', 'AUD',
].map((c) => ({ value: c, label: c }));

const MEASUREMENT_TYPES: SelectOption[] = [
  { value: String(QuotaMeasurementType.Revenue), label: 'QUOTAS.MEASUREMENT_REVENUE' },
  { value: String(QuotaMeasurementType.Margin), label: 'QUOTAS.MEASUREMENT_MARGIN' },
  { value: String(QuotaMeasurementType.Units), label: 'QUOTAS.MEASUREMENT_UNITS' },
  { value: String(QuotaMeasurementType.ACV), label: 'QUOTAS.MEASUREMENT_ACV' },
  { value: String(QuotaMeasurementType.Bookings), label: 'QUOTAS.MEASUREMENT_BOOKINGS' },
];

@Component({
  selector: 'app-quota-create',
  standalone: true,
  imports: [
    AppShellComponent,
    RouterLink,
    ReactiveFormsModule,
    TranslateModule,
    WsButtonComponent,
    WsInputComponent,
    WsSelectComponent,
    WsDateRangePickerComponent,
    WsPageHeaderComponent,
  ],
  templateUrl: './quota-create.component.html',
  styleUrl: './quota-create.component.scss',
})
export class QuotaCreateComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(QuotasStore);
  private readonly payeesStore = inject(PayeesStore);
  private readonly plansStore = inject(PlansStore);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly saving = signal(false);
  readonly currencies = CURRENCIES;
  readonly measurementTypes = MEASUREMENT_TYPES;

  readonly payeeOptions = signal<SelectOption[]>([]);
  readonly planOptions = signal<SelectOption[]>([]);

  readonly form = this.fb.nonNullable.group({
    payeeId: ['', Validators.required],
    planId: ['', Validators.required],
    measurementType: [String(QuotaMeasurementType.Revenue), Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    currency: ['USD', Validators.required],
    dateRange: [null as DateRange | null, Validators.required],
    notes: ['', Validators.maxLength(500)],
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([
      this.payeesStore.loadPayees(),
      this.plansStore.loadPlans(),
    ]);

    this.payeeOptions.set(
      this.payeesStore.payees()
        .filter((p) => p.status === PayeeStatus.Active)
        .map((p) => ({ value: p.id, label: `${p.fullName} (${p.employeeCode})` }))
    );

    this.planOptions.set(
      this.plansStore.plans()
        .filter((p) => p.status === 'Active' || p.status === 'Archived')
        .map((p) => ({ value: p.id, label: `${p.name} v${p.version}` }))
    );

    const preselectedPayeeId = this.route.snapshot.queryParamMap.get('payeeId');
    if (preselectedPayeeId) {
      this.form.patchValue({ payeeId: preselectedPayeeId });
    }
  }

  async onSubmit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const range = v.dateRange;
    if (!range?.start || !range?.end) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    try {
      const quota = await this.store.createQuota({
        payeeId: v.payeeId,
        planId: v.planId,
        measurementType: Number(v.measurementType) as QuotaMeasurementType,
        amount: v.amount,
        currency: v.currency,
        periodStart: range.start,
        periodEnd: range.end,
        notes: v.notes.trim() || null,
      });
      this.toast.show('QUOTAS.TOAST_CREATED', 'success');
      this.router.navigate(['/quotas', quota.id]);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.saving.set(false);
    }
  }

  hasError(field: string, error: string): boolean {
    const ctrl = this.form.get(field);
    return !!(ctrl?.touched && ctrl.hasError(error));
  }

  get rangeError(): string {
    const ctrl = this.form.get('dateRange');
    if (ctrl?.touched && ctrl.hasError('required')) return 'VALIDATION.REQUIRED';
    return '';
  }
}
