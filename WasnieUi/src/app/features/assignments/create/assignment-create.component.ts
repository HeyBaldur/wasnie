import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { AssignmentsStore } from '../state/assignments.store';
import { PayeesStore } from '../../payees/state/payees.store';
import { PlansStore } from '../../plans/state/plans.store';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { PayeeStatus } from '../../payees/models/payee.model';
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
  private readonly payeesStore = inject(PayeesStore);
  private readonly plansStore = inject(PlansStore);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly saving = signal(false);
  readonly payeeOptions = signal<SelectOption[]>([]);
  readonly planOptions = signal<SelectOption[]>([]);

  readonly form = this.fb.nonNullable.group({
    payeeId: ['', Validators.required],
    planId: ['', Validators.required],
    dateRange: [null as DateRange | null, Validators.required],
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
        .filter((p) => p.status === 'Active')
        .map((p) => ({ value: p.id, label: `${p.name} v${p.version}` }))
    );

    this.form.get('planId')?.valueChanges.subscribe((planId) => {
      if (!planId) return;
      const plan = this.plansStore.plans().find((p) => p.id === planId);
      if (plan) {
        this.form.patchValue({ dateRange: { start: plan.effectiveStart, end: plan.effectiveEnd } });
      }
    });

    const preselectedPayeeId = this.route.snapshot.queryParamMap.get('payeeId');
    if (preselectedPayeeId) this.form.patchValue({ payeeId: preselectedPayeeId });

    const preselectedPlanId = this.route.snapshot.queryParamMap.get('planId');
    if (preselectedPlanId) {
      this.form.patchValue({ planId: preselectedPlanId });
      const plan = this.plansStore.plans().find((p) => p.id === preselectedPlanId);
      if (plan) {
        this.form.patchValue({ dateRange: { start: plan.effectiveStart, end: plan.effectiveEnd } });
      }
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
