import { Component, inject, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, from, map } from 'rxjs';
import { UsersStore } from '../state/users.store';
import { AdminGrantWarningComponent } from '../access/admin-grant-warning.component';
import { TenantRole, UnlinkedPayee } from '../models/user.model';
import {
  WsButtonComponent,
  WsInputComponent,
  WsSelectComponent,
  type SelectOption,
} from '../../../shared/ui';

/**
 * How a payee is written in the picker, in ONE place.
 *
 * ★ THE EMPLOYEE CODE RIDES IN THE LABEL because two people share a name far more often than two
 * people share a code, and picking the wrong one is the failure this whole field exists to avoid.
 *
 * ★ SHARED BY THE SEARCH RESULTS AND THE ACCEPTED SUGGESTION, so the label on the closed trigger is
 * character-for-character the one that was in the list.
 */
function payeeOption(p: UnlinkedPayee): SelectOption {
  return {
    value: p.id,
    label: p.employeeCode ? `${p.fullName} · ${p.employeeCode}` : p.fullName,
  };
}

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
    AdminGrantWarningComponent,
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

  /** The payee whose address matches what was typed. Offered, never applied. */
  readonly suggested = signal<UnlinkedPayee | null>(null);

  /**
   * The option that keeps the accepted suggestion's NAME on the closed trigger.
   *
   * ★★ IN ASYNC MODE `ws-select` RESOLVES THE SELECTED LABEL FROM THE LAST SEARCH, and the suggested
   * payee is usually not in it — without this the control would hold the right id while the trigger
   * still read the placeholder, which looks exactly like the "Yes" button having done nothing.
   */
  readonly pickedPayee = signal<SelectOption | null>(null);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    // ★ No default role. Picking one for the admin is picking an authority level on their behalf,
    // and the cheapest one is as wrong a guess as the most powerful.
    role: ['' as TenantRole | '', [Validators.required]],
    payeeId: [''],
  });

  /** Only the roles that may be granted today — from the server, see `UsersStore.roleOptions`. */
  readonly roleOptions = this.store.roleOptions;

  /** Inviting somebody straight in as an administrator gets the same warning as promoting them. */
  readonly grantsAdmin = toSignal(
    this.form.controls.role.valueChanges.pipe(map((r) => r === 'TenantAdmin')),
    { initialValue: false },
  );

  /**
   * The picker's query, answered by the server.
   *
   * ★★ IT WAS A CLIENT-SIDE FILTER OVER EVERY UNLINKED PAYEE, AND THAT DID NOT SCALE (KAN-93). The
   * form loaded the whole roster on open and `[searchable]` filtered it in the browser: fine at ten
   * payees, unusable at a thousand, and it shipped every colleague's name, code and email address to
   * a screen that needed one of them. The server now filters and caps, exactly as every other payee
   * picker in this product does.
   */
  readonly payeeSearchFn = (q: string): Observable<SelectOption[]> =>
    from(this.store.loadUnlinkedPayees(undefined, q)).pipe(
      map(response => (response ? response.payees.map(payeeOption) : [])),
    );

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

    // ★ ONLY the suggestion. The list of names is the picker's own business now, and it asks for it
    // with whatever the admin types — not with the invitee's address.
    this.suggested.set(response.suggested);
  }

  /** The admin accepting the suggestion — the only way it ever reaches the form. */
  acceptSuggestion(): void {
    const payee = this.suggested();
    if (!payee) return;

    this.form.controls.payeeId.setValue(payee.id);
    // And the label with it — see `pickedPayee`.
    this.pickedPayee.set(payeeOption(payee));
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
        this.pickedPayee.set(null);
        this.invited.emit();
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
