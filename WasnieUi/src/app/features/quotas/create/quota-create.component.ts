import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, combineLatest, firstValueFrom, map, of, startWith, switchMap } from 'rxjs';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
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

/**
 * Pulls the per-payee reasons out of a refused batch. The server answers a rejected bulk create with
 * `{ created: [], failures: [{ payeeId, payeeName, payeeEmployeeCode, reason }] }` — a list, not a
 * sentence, because "one of them was wrong" is not something an admin can act on.
 */
function extractBatchFailures(
  err: unknown,
): { payeeName: string; payeeEmployeeCode: string; reason: string }[] {
  const body = (err as { error?: { failures?: unknown } } | null)?.error;
  const failures = body?.failures;
  if (!Array.isArray(failures)) return [];
  return failures.map(f => ({
    payeeName: String(f?.payeeName ?? ''),
    payeeEmployeeCode: String(f?.payeeEmployeeCode ?? ''),
    reason: String(f?.reason ?? ''),
  }));
}

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

  /** id → label for whatever the search has shown, so a chip can be built without a second fetch. */
  private readonly payeeLabels = new Map<string, string>();

  readonly payeeSearchFn = (q: string): Observable<SelectOption[]> =>
    this.payeesApi.getPayees({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => {
        const label = `${p.fullName} (${p.employeeCode})`;
        this.payeeLabels.set(p.id, label);
        return { value: p.id, label };
      }))
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

  /**
   * The payees this quota will be created for. ONE configuration, N people — the reason the batch
   * endpoint exists is that filling this form twenty times to say the same thing is where mistakes
   * come from.
   *
   * Chips + a search field rather than a new multi-select primitive: this is exactly how the payouts
   * filter already lets you pick several payees, and mirroring it beats inventing a widget.
   */
  readonly selectedPayees = signal<{ id: string; label: string }[]>([]);

  /** Per-payee reasons the last submit was refused. Empty until the server refuses a batch. */
  readonly batchFailures = signal<{ payeeName: string; payeeEmployeeCode: string; reason: string }[]>([]);

  readonly form = this.fb.nonNullable.group({
    // Holds the CURRENT search selection only; the batch is `selectedPayees`. Not required — the
    // submit guard checks the chips, because an empty search box with five chips is valid.
    payeeId: [''],
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

    // Arriving from a payee's profile ("set a quota for this person") pre-loads them as the first
    // chip — the batch of one, which is the old behaviour expressed in the new shape.
    if (payeeId) {
      if (payeeName) {
        const label = payeeCode ? `${payeeName} (${payeeCode})` : payeeName;
        this.preselectedPayeeOption.set({ value: payeeId, label });
        this.addPayee(payeeId, label);
      } else {
        firstValueFrom(this.payeesApi.getPayee(payeeId)).then(p => {
          const label = `${p.fullName} (${p.employeeCode})`;
          this.preselectedPayeeOption.set({ value: p.id, label });
          this.addPayee(p.id, label);
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

    // The search field is a PICKER, not a value: whatever it resolves becomes a chip and the field
    // clears itself for the next name. (ws-select is CVA-only — no valueChange output — so the
    // control's own stream is the event, exactly as the payouts filter does it.)
    this.form.controls.payeeId.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(value => this.onPayeeSelected(value));

    // Gap #2: warn (never block) when a selected payee has NO active assignment to the plan — their
    // attainment would sit at 0, because transactions are only credited to a (payee, plan) inside an
    // active assignment window.
    //
    // ONE request for the batch, not one per payee: the plan's assignments are fetched once and
    // intersected with the selection locally. Twenty payees must not mean twenty round trips.
    combineLatest([
      toObservable(this.selectedPayees),
      this.form.controls.planId.valueChanges.pipe(startWith(this.form.controls.planId.value)),
    ]).pipe(
      switchMap(([payees, planId]) => {
        if (payees.length === 0 || !planId) return of<boolean | null>(null);
        return this.assignmentsApi.getAssignmentsByPlan(planId, { page: 1, pageSize: 500 }).pipe(
          map(r => {
            const assigned = new Set(
              r.items.filter(a => a.status === 'Active').map(a => a.payeeId));
            return payees.every(p => assigned.has(p.id));
          }),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(allAssigned => {
      this.noActiveAssignment.set(allAssigned === false);
    });
  }

  /** Adds the payee the search field just resolved, then clears the field for the next one. */
  onPayeeSelected(payeeId: string | number | null): void {
    const id = payeeId ? String(payeeId) : '';
    if (!id) return;

    const label = this.payeeLabels.get(id) ?? id;
    this.addPayee(id, label);
    this.form.controls.payeeId.setValue('', { emitEvent: false });
  }

  private addPayee(id: string, label: string): void {
    // Silently ignore a repeat: the same person twice in one batch would create two identical
    // quotas, and the admin clicking a name they already picked means "include them", not "twice".
    if (this.selectedPayees().some(p => p.id === id)) return;
    this.selectedPayees.update(list => [...list, { id, label }]);
  }

  removePayee(id: string): void {
    this.selectedPayees.update(list => list.filter(p => p.id !== id));
  }

  async onSubmit(): Promise<void> {
    const payees = this.selectedPayees();
    if (this.form.invalid || payees.length === 0) {
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
    this.batchFailures.set([]);
    try {
      // One request for the whole batch — the server creates all of them or none, so there is no
      // half-applied state for this screen to explain or clean up.
      const result = await this.store.bulkCreateQuotas({
        payeeIds: payees.map(p => p.id),
        planId: v.planId,
        measurementType: Number(v.measurementType) as QuotaMeasurementType,
        amount: v.amount,
        currency: v.currency,
        periodStart: range.start,
        periodEnd: range.end,
        notes: v.notes.trim() || null,
      });

      this.toast.show('QUOTAS.TOAST_CREATED', 'success');
      this.router.navigateByUrl(
        this.returnTo() ?? (result.created.length === 1 ? `/quotas/${result.created[0].id}` : '/quotas'));
    } catch (err) {
      // A refused batch comes back with a reason per payee. Show them all: the admin has to know
      // which rows to fix before re-sending, and NOTHING was created, so re-sending is safe.
      const failures = extractBatchFailures(err);
      if (failures.length > 0) {
        this.batchFailures.set(failures);
        this.toast.show('QUOTAS.BULK_REJECTED', 'error');
      } else {
        this.toast.show(extractApiError(err), 'error');
      }
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
