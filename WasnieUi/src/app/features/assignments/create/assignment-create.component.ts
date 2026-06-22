import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, firstValueFrom, map, of, switchMap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { AssignmentsStore } from '../state/assignments.store';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import {
  WsButtonComponent,
  WsSelectComponent,
  WsDateRangePickerComponent,
  WsPageHeaderComponent,
  type SelectOption,
  type DateRange,
  type BadgeVariant,
} from '../../../shared/ui';

@Component({
  selector: 'app-assignment-create',
  standalone: true,
  imports: [
    AppShellComponent,
    RouterLink,
    ReactiveFormsModule,
    TranslateModule,
    WsButtonComponent,
    WsSelectComponent,
    WsDateRangePickerComponent,
    WsPageHeaderComponent,
  ],
  templateUrl: './assignment-create.component.html',
  styleUrl: './assignment-create.component.scss',
})
export class AssignmentCreateComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(AssignmentsStore);
  private readonly payeesApi = inject(PayeesApiService);
  private readonly plansApi = inject(PlansApiService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly _destroyRef = inject(DestroyRef);

  readonly saving = signal(false);
  readonly preselectedPayeeOption = signal<SelectOption | null>(null);
  readonly preselectedPlanOption = signal<SelectOption | null>(null);
  readonly returnTo = signal<string | null>(null);
  readonly planPeriodLocked = signal(false);

  readonly payeeSearchFn = (q: string): Observable<SelectOption[]> =>
    this.payeesApi.getPayees({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => ({ value: p.id, label: `${p.fullName} (${p.employeeCode})` })))
    );

  readonly planSearchFn = (q: string): Observable<SelectOption[]> =>
    this.plansApi.getPlans({ page: 1, pageSize: 20, search: q, filters: { status: 'Active' } }).pipe(
      map(r => r.items.map(p => ({
        value: p.id,
        label: `${p.name} v${p.version}`,
        badge: { text: 'PLANS.STATUS_ACTIVE', variant: 'success' as BadgeVariant },
      })))
    );

  readonly form = this.fb.nonNullable.group({
    payeeId: ['', Validators.required],
    planId: ['', Validators.required],
    dateRange: [null as DateRange | null, Validators.required],
  });

  constructor() {
    // The assignment period MUST equal the selected plan's effective period (it is the window
    // attainment is measured against). When a plan is chosen we copy its dates and LOCK the field;
    // when the plan is cleared we re-enable the field and clear the now-irrelevant value.
    this.form.controls.planId.valueChanges.pipe(
      // switchMap cancels any prior in-flight request if the user picks again quickly.
      switchMap(id => (id ? this.plansApi.getPlan(id) : of(null))),
      takeUntilDestroyed(this._destroyRef),
    ).subscribe(plan => {
      const dateRange = this.form.controls.dateRange;
      if (plan) {
        dateRange.setValue({ start: plan.effectiveStart, end: plan.effectiveEnd });
        dateRange.disable();
        this.planPeriodLocked.set(true);
        this.preselectedPlanOption.set({ value: plan.id, label: `${plan.name} v${plan.version}` });
      } else {
        dateRange.setValue(null);
        dateRange.enable();
        this.planPeriodLocked.set(false);
      }
    });
  }

  async ngOnInit(): Promise<void> {
    const snap = this.route.snapshot.queryParamMap;
    const payeeId   = snap.get('payeeId');
    const payeeName = snap.get('payeeName');
    const payeeCode = snap.get('payeeCode');
    const planId    = snap.get('planId');
    this.returnTo.set(snap.get('returnTo'));

    if (payeeId) {
      this.form.patchValue({ payeeId });
      if (payeeName) {
        // Label available immediately from queryParams — no async round-trip needed.
        const label = payeeCode ? `${payeeName} (${payeeCode})` : payeeName;
        this.preselectedPayeeOption.set({ value: payeeId, label });
      } else {
        firstValueFrom(this.payeesApi.getPayee(payeeId)).then(p => {
          this.preselectedPayeeOption.set({ value: p.id, label: `${p.fullName} (${p.employeeCode})` });
        });
      }
    }

    if (planId) {
      this.form.patchValue({ planId });
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
      await this.store.createAssignment({
        payeeId: v.payeeId,
        planId: v.planId,
        effectiveStart: range.start,
        effectiveEnd: range.end,
      });
      this.toast.show('ASSIGNMENTS.TOAST_CREATED', 'success');
      this.router.navigateByUrl(this.returnTo() ?? '/assignments');
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
