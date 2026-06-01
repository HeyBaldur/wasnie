import { Component, OnInit, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, map } from 'rxjs';
import { PayeesStore } from '../state/payees.store';
import { PayeesApiService } from '../services/payees.api.service';
import { SettingsApiService, FieldRequirement } from '../../admin/services/settings.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { Payee } from '../models/payee.model';
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
  private readonly payeesApi = inject(PayeesApiService);
  private readonly settingsApi = inject(SettingsApiService);
  private readonly toast = inject(ToastService);

  readonly payee = input<Payee | null>(null);
  readonly saved = output<Payee>();
  readonly cancelled = output<void>();

  readonly isEditMode = computed(() => this.payee() !== null);
  readonly saving = signal(false);

  readonly fieldRequirements = signal<FieldRequirement[]>([]);

  readonly emailRequired = computed(() =>
    this.fieldRequirements().find(r => r.fieldName === 'Email')?.isRequired ?? true
  );
  readonly hireDateRequired = computed(() =>
    this.fieldRequirements().find(r => r.fieldName === 'HireDate')?.isRequired ?? true
  );
  readonly roleRequired = computed(() =>
    this.fieldRequirements().find(r => r.fieldName === 'Role')?.isRequired ?? false
  );
  readonly managerRequired = computed(() =>
    this.fieldRequirements().find(r => r.fieldName === 'ManagerId')?.isRequired ?? false
  );
  readonly employmentTypeRequired = computed(() =>
    this.fieldRequirements().find(r => r.fieldName === 'EmploymentType')?.isRequired ?? false
  );
  readonly locationRequired = computed(() =>
    this.fieldRequirements().find(r => r.fieldName === 'Location')?.isRequired ?? false
  );

  readonly employmentTypeOptions: SelectOption[] = [
    { value: 'FullTime',   label: 'PAYEES.EMPLOYMENT_TYPE_FULLTIME' },
    { value: 'PartTime',   label: 'PAYEES.EMPLOYMENT_TYPE_PARTTIME' },
    { value: 'Temporary',  label: 'PAYEES.EMPLOYMENT_TYPE_TEMPORARY' },
    { value: 'Contractor', label: 'PAYEES.EMPLOYMENT_TYPE_CONTRACTOR' },
  ];

  readonly managerSearchFn = (q: string): Observable<SelectOption[]> =>
    this.payeesApi.getPayees({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => ({ value: p.id, label: `${p.fullName} (${p.employeeCode})` })))
    );

  readonly managerInitialOption = computed((): SelectOption | null => {
    const p = this.payee();
    if (!p?.managerId || !p.managerName) return null;
    return {
      value: p.managerId,
      label: p.managerEmployeeCode
        ? `${p.managerName} (${p.managerEmployeeCode})`
        : p.managerName,
    };
  });

  readonly form = this.fb.nonNullable.group({
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    employeeCode: ['', [Validators.required, Validators.maxLength(50)]],
    email: ['', [Validators.email, Validators.maxLength(255)]],
    hireDate: [''],
    role: ['', Validators.maxLength(100)],
    managerId: [''],
    employmentType: [''],
    location: ['', Validators.maxLength(200)],
  });

  constructor() {
    effect(() => {
      const p = this.payee();
      untracked(() => {
        if (p) {
          this.form.patchValue({
            fullName: p.fullName,
            employeeCode: p.employeeCode,
            email: p.email ?? '',
            hireDate: p.hireDate ?? '',
            role: p.role ?? '',
            managerId: p.managerId ?? '',
            employmentType: p.employmentType ?? '',
            location: p.location ?? '',
          });
        }
      });
    });

    // Sync required validators with settings
    effect(() => {
      const syncRequired = (ctrl: ReturnType<typeof this.form.get>, required: boolean) => {
        if (!ctrl) return;
        if (required) {
          ctrl.addValidators(Validators.required);
        } else {
          ctrl.removeValidators(Validators.required);
        }
        ctrl.updateValueAndValidity({ emitEvent: false });
      };

      syncRequired(this.form.controls.email,          this.emailRequired());
      syncRequired(this.form.controls.hireDate,        this.hireDateRequired());
      syncRequired(this.form.controls.role,            this.roleRequired());
      syncRequired(this.form.controls.managerId,       this.managerRequired());
      syncRequired(this.form.controls.employmentType,  this.employmentTypeRequired());
      syncRequired(this.form.controls.location,        this.locationRequired());
    });
  }

  ngOnInit(): void {
    this.settingsApi.getFieldRequirements().subscribe({
      next: (data) => this.fieldRequirements.set(data),
    });
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
      email: v.email.trim() || null,
      hireDate: v.hireDate || null,
      role: v.role.trim() || null,
      managerId: v.managerId || null,
      employmentType: v.employmentType || null,
      location: v.location.trim() || null,
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
}
