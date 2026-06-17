import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { PlansStore } from '../state/plans.store';
import { extractApiError } from '../../../shared/utils/api-error';
import { ToastService } from '../../../shared/services/toast.service';
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
  'USD', 'EUR', 'GBP', 'PLN', 'CAD', 'AUD', 'JPY', 'CHF',
].map(c => ({ value: c, label: c }));

@Component({
  selector: 'app-plan-create',
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
  templateUrl: './plan-create.component.html',
  styleUrl: './plan-create.component.scss',
})
export class PlanCreateComponent {
  private readonly fb = inject(FormBuilder);
  private readonly store = inject(PlansStore);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly saving = signal(false);
  readonly currencies = CURRENCIES;

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    description: ['', Validators.maxLength(500)],
    currency: ['EUR', Validators.required],
    dateRange: [null as DateRange | null, Validators.required],
  });

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
      const plan = await this.store.createPlan({
        name: v.name,
        description: v.description,
        currency: v.currency,
        effectiveStart: range.start,
        effectiveEnd: range.end,
      });
      this.toast.show('PLANS.TOAST_CREATED', 'success');
      this.router.navigate(['/plans', plan.id]);
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
