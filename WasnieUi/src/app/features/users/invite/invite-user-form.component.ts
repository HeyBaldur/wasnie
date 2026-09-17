import { Component, computed, inject, OnInit, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { UsersStore } from '../state/users.store';
import { TenantRole, UnlinkedPayee } from '../models/user.model';
import {
  WsButtonComponent,
  WsInputComponent,
  WsSelectComponent,
  type SelectOption,
} from '../../../shared/ui';

/**
 * The invite form (KAN-32, extended by KAN-92).
 *
 * ★★ ONE SHARED FORM COMPONENT, AS §5.6 REQUIRES — and it lives inside a ws-modal, never naked on the
 * page. The Payees form is the reference this mirrors: Ws primitives only, no native controls.
 *
 * ★★ THE PAYEE IS CHOSEN HERE AND NOWHERE ELSE (KAN-92). Granting access is the one moment a human
 * being states "this login is that payee", so it is the one moment the link may be established. The
 * server offers only payees nobody owns yet, and may point at one whose address matches — but this
 * form NEVER pre-selects it. A suggestion accepted by reflex shows one person another person's pay,
 * and addresses coincide for boring reasons: a shared mailbox, a replacement in the same seat.
 *
 * ★ THE FIELD IS OPTIONAL. An administrator or a finance user does not get paid and has no payee; a
 * required picker would force whoever invites them to attach somebody at random.
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
export class InviteUserFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  readonly store = inject(UsersStore);

  readonly invited = output<void>();
  readonly cancelled = output<void>();

  readonly submitting = signal(false);

  private readonly unlinkedPayees = signal<UnlinkedPayee[]>([]);

  /** The payee whose address matches what was typed. Offered, never applied. */
  readonly suggested = signal<UnlinkedPayee | null>(null);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    // ★ No default role. Picking one for the admin is picking an authority level on their behalf,
    // and the cheapest of the four is as wrong a guess as the most powerful.
    role: ['' as TenantRole | '', [Validators.required]],
    payeeId: [''],
  });

  readonly roleOptions = computed<SelectOption[]>(() => [
    { value: 'TenantAdmin', label: 'USERS.ROLES.TENANT_ADMIN' },
    { value: 'CompManager', label: 'USERS.ROLES.COMP_MANAGER' },
    { value: 'Manager', label: 'USERS.ROLES.MANAGER' },
    { value: 'Rep', label: 'USERS.ROLES.REP' },
  ]);

  /**
   * The picker's options. The employee code rides in the label because two people share a name far
   * more often than two people share a code, and picking the wrong one is the failure this whole
   * field exists to avoid.
   */
  readonly payeeOptions = computed<SelectOption[]>(() =>
    this.unlinkedPayees().map((p) => ({
      value: p.id,
      label: p.employeeCode ? `${p.fullName} · ${p.employeeCode}` : p.fullName,
    })),
  );

  async ngOnInit(): Promise<void> {
    const response = await this.store.loadUnlinkedPayees();
    if (response) this.unlinkedPayees.set(response.payees);
  }

  /**
   * Asks the server whether any unlinked payee uses this address.
   *
   * ★ IT SETS `suggested` AND NOTHING ELSE. The form control is left exactly as the admin left it.
   */
  async onEmailChange(email: string): Promise<void> {
    if (!email.includes('@')) {
      this.suggested.set(null);
      return;
    }

    const response = await this.store.loadUnlinkedPayees(email.trim());
    if (!response) return;

    this.unlinkedPayees.set(response.payees);
    this.suggested.set(response.suggested);
  }

  /** The admin accepting the suggestion — the only way it ever reaches the form. */
  acceptSuggestion(): void {
    const payee = this.suggested();
    if (payee) this.form.controls.payeeId.setValue(payee.id);
  }

  async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    try {
      const { email, role, payeeId } = this.form.getRawValue();
      const ok = await this.store.invite(email.trim(), role as TenantRole, payeeId || null);

      // ★ The form closes only on success. A failure has already surfaced its coded refusal through
      // the toast, and closing would take the typed address away with it — leaving the admin to
      // retype an address the system just rejected for a reason they may want to re-read.
      if (ok) {
        this.form.reset({ email: '', role: '', payeeId: '' });
        this.suggested.set(null);
        this.invited.emit();
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
