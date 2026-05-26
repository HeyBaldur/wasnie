import { Component, computed, effect, inject, input, OnInit, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { PayeesStore } from '../state/payees.store';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { Payee, PayeeStatus } from '../models/payee.model';
import {
  WsButtonComponent,
  WsInputComponent,
  WsSelectComponent,
  WsDatePickerComponent,
  type SelectOption,
} from '../../../shared/ui';

@Component({
  selector: 'app-payee-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TranslateModule,
    WsButtonComponent,
    WsInputComponent,
    WsSelectComponent,
    WsDatePickerComponent,
  ],
  templateUrl: './payee-form.component.html',
  styleUrl: './payee-form.component.scss',
})
export class PayeeFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly store = inject(PayeesStore);
  private readonly toast = inject(ToastService);

  readonly payee = input<Payee | null>(null);
  readonly saved = output<Payee>();
  readonly cancelled = output<void>();

  readonly isEditMode = computed(() => this.payee() !== null);
  readonly saving = signal(false);
  readonly managerOptions = signal<SelectOption[]>([]);

  readonly form = this.fb.nonNullable.group({
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    employeeCode: ['', [Validators.required, Validators.maxLength(50)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(255)]],
    hireDate: ['', Validators.required],
    role: ['', Validators.maxLength(100)],
    managerId: [''],
  });

  constructor() {
    effect(() => {
      const p = this.payee();
      untracked(() => {
        if (p) {
          this.form.patchValue({
            fullName: p.fullName,
            employeeCode: p.employeeCode,
            email: p.email,
            hireDate: p.hireDate,
            role: p.role ?? '',
            managerId: p.managerId ?? '',
          });
          this._rebuildManagerOptions(p.id);
        }
      });
    });
  }

  async ngOnInit(): Promise<void> {
    await this.store.loadPayees();
    this._rebuildManagerOptions(this.payee()?.id ?? null);
  }

  async onSubmit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const payload = {
      fullName: v.fullName.trim(),
      employeeCode: v.employeeCode.trim(),
      email: v.email.trim(),
      hireDate: v.hireDate,
      role: v.role.trim() || null,
      managerId: v.managerId || null,
    };
    this.saving.set(true);
    try {
      let result: Payee;
      const current = this.payee();
      if (current) {
        result = await this.store.updatePayee(current.id, { payeeId: current.id, ...payload });
        this.toast.show('PAYEES.TOAST_UPDATED', 'success');
      } else {
        result = await this.store.createPayee(payload);
        this.toast.show('PAYEES.TOAST_CREATED', 'success');
      }
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

  get fullNameError(): string {
    if (this.hasError('fullName', 'required')) return 'VALIDATION.REQUIRED';
    if (this.hasError('fullName', 'maxlength')) return 'VALIDATION.INVALID';
    return '';
  }

  private _rebuildManagerOptions(excludeId: string | null): void {
    this.managerOptions.set(
      this.store.payees()
        .filter((p) =>
          (p.status === PayeeStatus.Active || p.status === PayeeStatus.OnLeave) &&
          p.id !== excludeId
        )
        .map((p) => ({ value: p.id, label: `${p.fullName} (${p.employeeCode})` }))
    );
  }
}
