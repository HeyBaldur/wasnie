import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, combineLatest, firstValueFrom, map, of, startWith, switchMap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { QuotasStore } from '../state/quotas.store';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { AssignmentsApiService } from '../../assignments/services/assignments.api.service';
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
  type BadgeVariant,
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
    IconComponent,
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
  private readonly assignmentsApi = inject(AssignmentsApiService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly saving = signal(false);
  readonly measurementTypes = MEASUREMENT_TYPES;
  readonly preselectedPayeeOption = signal<SelectOption | null>(null);
  readonly returnTo = signal<string | null>(null);
  readonly planCurrencyLocked = signal(false);
  // Gap #2 (non-blocking UX warning): true when the selected payee has NO active assignment to the
  // selected plan. The quota can still be created, but its attainment will stay at 0 until the
  // assignment exists, because transactions are only credited to a (payee, plan) within an
  // active assignment window. We warn, we do not block (owner decision).
  readonly noActiveAssignment = signal(false);
  // The selected plan's effective period — drives the auto-fill default and the containment
  // validator. The quota period stays editable but must fall within these bounds.
  readonly planPeriod = signal<{ start: string; end: string } | null>(null);

  // Quota period must be contained within the selected plan's effective period.
  // ISO yyyy-MM-dd strings compare correctly lexicographically.
  private readonly periodWithinPlanValidator: ValidatorFn = (control: AbstractControl) => {
    const period = this.planPeriod();
    const range = control.value as DateRange | null;
    if (!period || !range?.start || !range?.end) return null;
    return range.start < period.start || range.end > period.end
      ? { outsidePlanPeriod: true }
      : null;
  };

  readonly payeeSearchFn = (q: string): Observable<SelectOption[]> =>
    this.payeesApi.getPayees({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => ({ value: p.id, label: `${p.fullName} (${p.employeeCode})` })))
    );

  readonly planSearchFn = (q: string): Observable<SelectOption[]> =>
    this.plansApi.getPlans({ page: 1, pageSize: 20, search: q, filters: { statuses: 'Active,Archived' } }).pipe(
      map(r => r.items.map(p => ({
        value: p.id,
        label: `${p.name} v${p.version}`,
        badge: {
          text: `PLANS.STATUS_${p.status.toUpperCase()}`,
          variant: (p.status === 'Active' ? 'success' : 'neutral') as BadgeVariant,
        },
      })))
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

    this.form.controls.dateRange.addValidators(this.periodWithinPlanValidator);

    // When a plan is selected: lock the currency to the plan's currency, and default the period
    // to the plan's effective window (the period stays EDITABLE but is validated to remain within
    // that window — see periodWithinPlanValidator). switchMap cancels stale in-flight requests.
    this.form.controls.planId.valueChanges.pipe(
      switchMap(planId => (planId ? this.plansApi.getPlan(planId) : of(null))),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(plan => {
      if (plan) {
        this.form.controls.currency.setValue(plan.currency);
        this.planCurrencyLocked.set(true);
        this.planPeriod.set({ start: plan.effectiveStart, end: plan.effectiveEnd });
        this.form.controls.dateRange.setValue({ start: plan.effectiveStart, end: plan.effectiveEnd });
      } else {
        this.form.controls.currency.setValue('');
        this.planCurrencyLocked.set(false);
        this.planPeriod.set(null);
      }
      this.form.controls.dateRange.updateValueAndValidity();
    });

    // Gap #2: once both payee and plan are chosen, check whether the payee has an active
    // assignment to that plan. If not, surface a non-blocking warning. switchMap cancels
    // stale lookups when the selection changes again.
    combineLatest([
      this.form.controls.payeeId.valueChanges.pipe(startWith(this.form.controls.payeeId.value)),
      this.form.controls.planId.valueChanges.pipe(startWith(this.form.controls.planId.value)),
    ]).pipe(
      switchMap(([payeeId, planId]) => {
        if (!payeeId || !planId) return of<boolean | null>(null);
        return this.assignmentsApi.getAssignmentsByPayee(payeeId, { page: 1, pageSize: 100 }).pipe(
          map(r => r.items.some(a => a.planId === planId && a.status === 'Active')),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(hasActiveAssignment => {
      this.noActiveAssignment.set(hasActiveAssignment === false);
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

  /// True when the chosen period falls outside the selected plan's effective window. Drives both
  /// the field error and the disabled state of the submit button, so the button can never look
  /// actionable while that error is on screen. Deliberately independent of `touched`: the plan
  /// auto-fills the range, so a later plan change can invalidate an untouched range too.
  periodOutsidePlan(): boolean {
    return this.form.controls.dateRange.hasError('outsidePlanPeriod');
  }

  get rangeError(): string {
    const ctrl = this.form.get('dateRange');
    if (ctrl?.touched && ctrl.hasError('required')) return 'VALIDATION.REQUIRED';
    if (ctrl?.touched && ctrl.hasError('outsidePlanPeriod')) return 'QUOTAS.PERIOD_OUTSIDE_PLAN';
    return '';
  }
}
