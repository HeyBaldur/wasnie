import { Component, computed, inject, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { UsersStore } from '../state/users.store';
import { TenantRole } from '../models/user.model';
import {
  WsButtonComponent,
  WsInputComponent,
  WsSelectComponent,
  type SelectOption,
} from '../../../shared/ui';

/**
 * The invite form (KAN-32).
 *
 * ★★ ONE SHARED FORM COMPONENT, AS §5.6 REQUIRES — and it lives inside a ws-modal, never naked on the
 * page. The Payees form is the reference this mirrors: Ws primitives only, no native <select>, no
 * native <input type="email"> styling of our own.
 *
 * ★ TWO FIELDS, SO NO TWO-COLUMN GRID. The `.ws-form-grid` rule starts at four fields; forcing two
 * into columns would leave a half-empty row.
 */
@Component({
  selector: 'app-invite-user-form',
  standalone: true,
  imports: [
    ReactiveFormsModule, TranslateModule,
    WsButtonComponent, WsInputComponent, WsSelectComponent,
  ],
  templateUrl: './invite-user-form.component.html',
  styleUrl: './invite-user-form.component.scss',
})
export class InviteUserFormComponent {
  private readonly fb = inject(FormBuilder);
  readonly store = inject(UsersStore);

  readonly invited = output<void>();
  readonly cancelled = output<void>();

  readonly submitting = signal(false);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    // ★ No default role. Picking one for the admin is picking an authority level on their behalf,
    // and the cheapest of the four is as wrong a guess as the most powerful.
    role: ['' as TenantRole | '', [Validators.required]],
  });

  readonly roleOptions = computed<SelectOption[]>(() => [
    { value: 'TenantAdmin', label: 'USERS.ROLES.TENANT_ADMIN' },
    { value: 'CompManager', label: 'USERS.ROLES.COMP_MANAGER' },
    { value: 'Manager', label: 'USERS.ROLES.MANAGER' },
    { value: 'Rep', label: 'USERS.ROLES.REP' },
  ]);

  async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    try {
      const { email, role } = this.form.getRawValue();
      const ok = await this.store.invite(email.trim(), role as TenantRole);

      // ★ The form closes only on success. A failure has already surfaced its coded refusal through
      // the interceptor's toast, and closing would take the typed address away with it — leaving the
      // admin to retype an address the system just rejected for a reason they may want to re-read.
      if (ok) {
        this.form.reset({ email: '', role: '' });
        this.invited.emit();
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
