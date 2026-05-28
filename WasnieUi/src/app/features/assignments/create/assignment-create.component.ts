import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, filter, firstValueFrom, map, switchMap } from 'rxjs';
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

  readonly payeeSearchFn = (q: string): Observable<SelectOption[]> =>
    this.payeesApi.getPayees({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => ({ value: p.id, label: `${p.fullName} (${p.employeeCode})` })))
    );

  readonly planSearchFn = (q: string): Observable<SelectOption[]> =>
    this.plansApi.getPlans({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items
        .filter(p => p.status === 'Active')
        .map(p => ({ value: p.id, label: `${p.name} v${p.version}` }))
      )
    );

  readonly form = this.fb.nonNullable.group({
    payeeId: ['', Validators.required],
    planId: ['', Validators.required],
    dateRange: [null as DateRange | null, Validators.required],
  });

  constructor() {
    // When planId changes, fetch the full plan to auto-fill the date range.
    // switchMap cancels any prior in-flight request if the user picks again quickly.
    this.form.get('planId')!.valueChanges.pipe(
      filter(Boolean),
      switchMap(id => this.plansApi.getPlan(id)),
      takeUntilDestroyed(this._destroyRef),
    ).subscribe(plan => {
      this.form.patchValue({ dateRange: { start: plan.effectiveStart, end: plan.effectiveEnd } });
      this.preselectedPlanOption.set({ value: plan.id, label: `${plan.name} v${plan.version}` });
    });
  }

  async ngOnInit(): Promise<void> {
    const payeeId = this.route.snapshot.queryParamMap.get('payeeId');
    const planId = this.route.snapshot.queryParamMap.get('planId');

    if (payeeId) {
      this.form.patchValue({ payeeId });
      firstValueFrom(this.payeesApi.getPayee(payeeId)).then(p => {
        this.preselectedPayeeOption.set({ value: p.id, label: `${p.fullName} (${p.employeeCode})` });
      });
    }

    if (planId) {
      // Patching planId triggers the constructor subscription which fetches the plan,
      // sets dateRange, and sets preselectedPlanOption.
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
      this.router.navigate(['/assignments']);
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
