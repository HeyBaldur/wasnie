import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, firstValueFrom, map, switchMap, filter } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { QuotasStore } from '../state/quotas.store';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
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

// V1: only Revenue and Units are supported (transaction data model).
// Margin, ACV, Bookings require additional transaction fields — activate in a future WI.
const MEASUREMENT_TYPES: SelectOption[] = [
  { value: String(QuotaMeasurementType.Revenue), label: 'QUOTAS.MEASUREMENT_REVENUE' },
  { value: String(QuotaMeasurementType.Units), label: 'QUOTAS.MEASUREMENT_UNITS' },
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
  private readonly payeesApi = inject(PayeesApiService);
  private readonly plansApi = inject(PlansApiService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly saving = signal(false);
  readonly measurementTypes = MEASUREMENT_TYPES;
  readonly preselectedPayeeOption = signal<SelectOption | null>(null);
  readonly returnTo = signal<string | null>(null);
  readonly planCurrencyLocked = signal(false);

  readonly payeeSearchFn = (q: string): Observable<SelectOption[]> =>
    this.payeesApi.getPayees({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => ({ value: p.id, label: `${p.fullName} (${p.employeeCode})` })))
    );

  readonly planSearchFn = (q: string): Observable<SelectOption[]> =>
    this.plansApi.getPlans({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items
        .filter(p => p.status === 'Active' || p.status === 'Archived')
        .map(p => ({ value: p.id, label: `${p.name} v${p.version}` }))
      )
    );

  readonly form = this.fb.nonNullable.group({
    payeeId: ['', Validators.required],
    planId: ['', Validators.required],
    measurementType: [String(QuotaMeasurementType.Revenue), Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    currency: [{ value: '', disabled: true }, Validators.required],
    dateRange: [null as DateRange | null, Validators.required],
    notes: ['', Validators.maxLength(500)],
  });

  async ngOnInit(): Promise<void> {
    const snap = this.route.snapshot.queryParamMap;
    const payeeId   = snap.get('payeeId');
    const payeeName = snap.get('payeeName');
    const payeeCode = snap.get('payeeCode');
    this.returnTo.set(snap.get('returnTo'));

    if (payeeId) {
      this.form.patchValue({ payeeId });
      if (payeeName) {
        const label = payeeCode ? `${payeeName} (${payeeCode})` : payeeName;
        this.preselectedPayeeOption.set({ value: payeeId, label });
      } else {
        firstValueFrom(this.payeesApi.getPayee(payeeId)).then(p => {
          this.preselectedPayeeOption.set({ value: p.id, label: `${p.fullName} (${p.employeeCode})` });
        });
      }
    }

    // When a plan is selected, auto-set the currency to the plan's currency (field stays disabled).
    this.form.controls.planId.valueChanges.pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(planId => {
      if (!planId) {
        this.form.controls.currency.setValue('');
        this.planCurrencyLocked.set(false);
        return;
      }
      this.plansApi.getPlan(planId).pipe(
        takeUntilDestroyed(this.destroyRef)
      ).subscribe(plan => {
        this.form.controls.currency.setValue(plan.currency);
        this.planCurrencyLocked.set(true);
      });
    });
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
      this.router.navigateByUrl(this.returnTo() ?? `/quotas/${quota.id}`);
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
