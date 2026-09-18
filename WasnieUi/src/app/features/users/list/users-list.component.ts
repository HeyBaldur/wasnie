import { Component, HostListener, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, from, map } from 'rxjs';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { createRowMenu } from '../../../shared/utils/row-menu';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { UsersStore } from '../state/users.store';
import { Invitation, InvitationStatus, TenantRole, TenantUser, UnlinkedPayee } from '../models/user.model';
import { InviteUserFormComponent } from '../invite/invite-user-form.component';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsPageLayoutComponent,
  WsTableComponent,
  WsTableEmptyComponent,
  WsModalComponent,
  WsConfirmationModalComponent,
  WsSelectComponent,
  type SelectOption,
} from '../../../shared/ui';

/**
 * How a payee is written in the picker, in ONE place.
 *
 * ★ THE EMPLOYEE CODE RIDES IN THE LABEL because two people share a name far more often than two
 * people share a code, and picking the wrong one is the failure this whole field exists to avoid. Not
 * translated: these are people's names and their employer's own codes.
 *
 * ★ SHARED BY THE SEARCH RESULTS AND THE ACCEPTED SUGGESTION, so the label on the closed trigger is
 * character-for-character the one that was in the list.
 */
function payeeOption(p: UnlinkedPayee): SelectOption {
  return {
    value: p.id,
    label: p.employeeCode ? `${p.fullName} (${p.employeeCode})` : p.fullName,
  };
}

/**
 * Who has access to this tenant, and which invitations are still outstanding (KAN-32).
 *
 * ★★ THE ACTIONS ARE HIDDEN, NOT DISABLED (§5.8). A CompManager holds Users.Read and not
 * Users.Manage: they see the roster, and the Invite button, the role picker and the deactivate
 * action are simply not in their DOM. A greyed-out button that explains what somebody may not do is
 * a worse answer than a page that only offers what they can.
 *
 * ★★ THE SEAT COUNTER ONLY APPEARS WHEN THERE IS A LIMIT. Every plan today is unlimited, so the
 * normal rendering of this screen has no counter at all — "3 of unlimited" is noise pretending to be
 * information. The markup exists because the parameter does, and the day a capped tier is configured
 * the counter appears with no code change.
 *
 * ★★ NOTHING IS OFFERED ON YOUR OWN ROW (KAN-93, §5.8). The server has always refused an
 * administrator acting on themselves — `USER_CANNOT_ACT_ON_SELF`, and `LastAdminGuard` behind it —
 * but the menu offered "Change role", "Deactivate" and "Remove" anyway, so the only way to learn was
 * to try, watch the dialog close and read a toast. Offering a control whose answer is always no is
 * the pattern §5.8 exists to end. The refusal stays in the backend: it is the invariant, and this is
 * only the screen agreeing with it.
 *
 * ★ DEACTIVATED PEOPLE STAY ON THE LIST, GREYED, RATHER THAN DISAPPEARING. They still hold the audit
 * trail's references (§B6), and a screen that hid them would make an admin think the account was
 * deleted — which is the one thing that never happens here.
 */
@Component({
  selector: 'app-users-list',
  standalone: true,
  imports: [
    AppShellComponent, TranslateModule, ReactiveFormsModule, IconComponent, DateFormatPipe,
    HasPermissionDirective, HasPermissionPipe,
    InviteUserFormComponent,
    WsButtonComponent, WsBadgeComponent, WsPageLayoutComponent,
    WsTableComponent, WsTableEmptyComponent, WsModalComponent,
    WsConfirmationModalComponent, WsSelectComponent,
  ],
  templateUrl: './users-list.component.html',
  styleUrl: './users-list.component.scss',
})
export class UsersListComponent implements OnInit {
  readonly store = inject(UsersStore);

  private readonly fb = inject(FormBuilder);
  private readonly currentUser = inject(CurrentUserService);

  // ★ The shared row-menu controller, not a local signal. It anchors the dropdown to the row on
  // scroll by listening on `window` IN CAPTURE — the page does not scroll the window and scroll does
  // not bubble, which is the detail every hand-rolled copy of this has got wrong.
  private readonly rowMenu = createRowMenu();
  readonly openMenuId = this.rowMenu.openMenuId;
  readonly menuPosition = this.rowMenu.menuPosition;

  readonly inviteOpen = signal(false);
  readonly changeRoleFor = signal<TenantUser | null>(null);

  /** One control for the whole page, reused by whichever row opened the modal. */
  readonly roleForm = this.fb.nonNullable.group({
    role: ['' as TenantRole | '', [Validators.required]],
  });
  readonly confirmDeactivate = signal<TenantUser | null>(null);
  readonly confirmRemove = signal<TenantUser | null>(null);
  readonly confirmRevoke = signal<Invitation | null>(null);
  readonly showClosedInvitations = signal(false);

  /** The role picker's options, translated. Labels live in i18n, values are Identity's names. */
  readonly roleOptions = computed<SelectOption[]>(() => [
    { value: 'TenantAdmin', label: 'USERS.ROLES.TENANT_ADMIN' },
    { value: 'CompManager', label: 'USERS.ROLES.COMP_MANAGER' },
    { value: 'Manager', label: 'USERS.ROLES.MANAGER' },
    { value: 'Rep', label: 'USERS.ROLES.REP' },
  ]);

  // ── The payee link (KAN-93) ───────────────────────────────────────────────────────────────────
  //
  // ★★ A TYPEAHEAD AGAINST THE SERVER, NOT A LIST IN A DROPDOWN. The first cut loaded every unlinked
  // payee and rendered them all: fine at ten, unusable at a thousand, and it shipped the whole staff
  // roster — names, codes and email addresses — on every modal open. `ws-select` takes a `searchFn`
  // for exactly this, and every other payee picker in the product (quotas, credits, payouts, the
  // payee form's manager field) already uses it. §5.1: mirror what works rather than invent.
  readonly linkPayeeFor = signal<TenantUser | null>(null);
  readonly payeesLoading = signal(false);

  /**
   * How many unlinked payees exist AT ALL, whatever is typed in the box.
   *
   * ★★ IT IS NOT A COUNT OF THE DROPDOWN. "No payee is free — unlink one first" and "your search
   * matched nothing" are opposite problems with opposite fixes, and the search results cannot tell
   * them apart. `ws-select` already says "no results" for the second; this signal is the only thing
   * that can say the first.
   */
  readonly totalAvailablePayees = signal<number | null>(null);

  /**
   * ★ THE SUGGESTION IS SHOWN, NEVER SELECTED. The server points at the payee whose address matches,
   * and the administrator is the one who picks — a wrong match accepted by reflex hands one person
   * another person's pay. This is the same rule the invite form follows.
   */
  readonly suggestedPayee = signal<UnlinkedPayee | null>(null);

  /**
   * The option that resolves the chosen payee's NAME on the closed trigger.
   *
   * ★★ WITHOUT IT, ACCEPTING THE SUGGESTION LOOKS LIKE IT DID NOTHING. In async mode `ws-select`
   * resolves the selected label from whatever the last search returned, and the suggested payee is
   * usually not in it — the control would hold the right id while the trigger still read "Choose a
   * payee". `initialOption` is the primitive's own answer to that, and this is what feeds it.
   */
  readonly pickedPayeeOption = signal<SelectOption | null>(null);

  readonly linkPayeeForm = this.fb.nonNullable.group({
    payeeId: ['', [Validators.required]],
  });

  readonly confirmUnlink = signal<TenantUser | null>(null);

  /** True only once the server has said there is genuinely nobody left to attach. */
  readonly noPayeesAvailable = computed(() => this.totalAvailablePayees() === 0);

  /**
   * The picker's query. Debounced by `ws-select`, filtered and capped by the server.
   *
   * ★ THE TOTAL IS RE-READ ON EVERY SEARCH, so the "nobody is free" notice stays true while another
   * administrator links somebody with this dialog open.
   */
  readonly payeeSearchFn = (q: string): Observable<SelectOption[]> =>
    from(this.store.loadUnlinkedPayees(undefined, q)).pipe(
      map(response => {
        if (!response) return [];
        this.totalAvailablePayees.set(response.totalAvailable);
        return response.payees.map(payeeOption);
      }),
    );

  ngOnInit(): void {
    void this.store.load();
  }

  /**
   * Whether this row is the signed-in administrator.
   *
   * ★★ IT IS THE USER ID, NOT THE EMAIL. Addresses are editable and can coincide; the id is what the
   * server compares in `CannotActOnSelf`, and the screen must hide exactly what the server refuses or
   * the two are answering different questions.
   */
  isSelf(user: TenantUser): boolean {
    return this.currentUser.currentUser()?.userId === user.userId;
  }

  /**
   * Opens the dialog and asks the two questions the dropdown cannot: is there an unlinked payee with
   * this person's address, and are there any unlinked payees at all.
   *
   * ★ IT DOES NOT FETCH A PAGE OF NAMES. `ws-select` runs `payeeSearchFn` itself when it opens, so
   * loading one here would be the same request made twice and one of the answers thrown away.
   */
  async openLinkPayee(user: TenantUser): Promise<void> {
    this.rowMenu.close();
    this.linkPayeeForm.reset({ payeeId: '' });
    this.suggestedPayee.set(null);
    this.pickedPayeeOption.set(null);
    this.totalAvailablePayees.set(null);
    this.linkPayeeFor.set(user);
    this.payeesLoading.set(true);

    // The address is a HINT for the server's suggestion and nothing more — it never selects.
    const response = await this.store.loadUnlinkedPayees(user.email);
    this.payeesLoading.set(false);
    if (!response) return;

    this.suggestedPayee.set(response.suggested);
    this.totalAvailablePayees.set(response.totalAvailable);
  }

  /** Pressing the suggestion fills the picker. It is still the administrator who pressed it. */
  acceptSuggestion(): void {
    const payee = this.suggestedPayee();
    if (!payee) return;

    this.linkPayeeForm.patchValue({ payeeId: payee.id });
    // ★ And the label with it — see `pickedPayeeOption`. Setting only the id leaves the trigger
    // reading the placeholder, which looks exactly like the button having done nothing.
    this.pickedPayeeOption.set(payeeOption(payee));
  }

  async submitLinkPayee(): Promise<void> {
    const user = this.linkPayeeFor();
    if (!user || this.linkPayeeForm.invalid) {
      this.linkPayeeForm.markAllAsTouched();
      return;
    }

    // ★ Closes only on success, like the role dialog: a refusal — the payee taken by somebody else
    // between loading the picker and pressing save — must stay attached to the row it is about.
    if (await this.store.linkPayee(user.userId, this.linkPayeeForm.getRawValue().payeeId)) {
      this.linkPayeeFor.set(null);
      this.pickedPayeeOption.set(null);
    }
  }

  askUnlinkPayee(user: TenantUser): void {
    this.rowMenu.close();
    this.confirmUnlink.set(user);
  }

  async confirmUnlinkPayee(): Promise<void> {
    const user = this.confirmUnlink();
    if (!user) return;
    if (await this.store.linkPayee(user.userId, null)) this.confirmUnlink.set(null);
  }

  /** The i18n key for a role name, so the table never prints "CompManager" at a human. */
  roleKey(role: TenantRole | null): string {
    switch (role) {
      case 'TenantAdmin': return 'USERS.ROLES.TENANT_ADMIN';
      case 'CompManager': return 'USERS.ROLES.COMP_MANAGER';
      case 'Manager': return 'USERS.ROLES.MANAGER';
      case 'Rep': return 'USERS.ROLES.REP';
      default: return 'USERS.ROLES.UNKNOWN';
    }
  }

  toggleMenu(id: string, event: MouseEvent): void {
    this.rowMenu.toggle(id, event);
  }

  /**
   * Any click that is not the trigger closes the menu.
   *
   * ★ THE CONTROLLER DOES NOT DO THIS, and that is not an oversight in it: it owns anchoring to the
   * row, and the component owns dismissal, exactly as the Payees list has it. `toggle` stops the
   * event, so the click that OPENS a menu never reaches this listener.
   */
  @HostListener('document:click')
  closeMenu(): void {
    this.rowMenu.close();
  }

  /** Opening a confirmation closes the menu that launched it, or it hangs over the dialog. */
  askDeactivate(user: TenantUser): void {
    this.rowMenu.close();
    this.confirmDeactivate.set(user);
  }

  askRevoke(invitation: Invitation): void {
    this.rowMenu.close();
    this.confirmRevoke.set(invitation);
  }

  /** Initials for the avatar chip — the same two-letter shape the Payees list uses. */
  initialsFor(user: TenantUser): string {
    const source = this.displayName(user).trim();
    const parts = source.split(/\s+/).filter(Boolean);
    if (parts.length === 0) return '?';
    if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
    return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
  }

  displayName(user: TenantUser): string {
    const name = [user.firstName, user.lastName].filter(Boolean).join(' ').trim();
    return name.length > 0 ? name : user.email;
  }

  async onInvited(): Promise<void> {
    this.inviteOpen.set(false);
  }

  openRoleChange(user: TenantUser): void {
    this.rowMenu.close();
    this.roleForm.reset({ role: user.role ?? '' });
    this.changeRoleFor.set(user);
  }

  async submitRoleChange(): Promise<void> {
    const user = this.changeRoleFor();
    if (!user || this.roleForm.invalid) {
      this.roleForm.markAllAsTouched();
      return;
    }

    const role = this.roleForm.getRawValue().role as TenantRole;
    if (role === user.role) {
      this.changeRoleFor.set(null);
      return;
    }

    // ★ Closes only on success. A refusal — the last administrator, or acting on yourself — has
    // already been surfaced by the interceptor, and closing would hide which row it was about.
    if (await this.store.changeRole(user.userId, role)) {
      this.changeRoleFor.set(null);
    }
  }

  /**
   * The i18n key for an invitation status.
   *
   * ★★ A WHITELIST, NOT `'USERS.INVITATION_STATUS.' + status` (§C2). A concatenated key for a status
   * this build does not know would print the raw identifier into the table.
   */
  invitationStatusKey(status: InvitationStatus): string {
    switch (status) {
      case 'Pending': return 'USERS.INVITATION_STATUS.PENDING';
      case 'Accepted': return 'USERS.INVITATION_STATUS.ACCEPTED';
      case 'Expired': return 'USERS.INVITATION_STATUS.EXPIRED';
      case 'Revoked': return 'USERS.INVITATION_STATUS.REVOKED';
      default: return 'USERS.INVITATION_STATUS.PENDING';
    }
  }

  /**
   * ★ CLOSES ONLY ON SUCCESS. Closing either way is what made the refusal invisible in the first
   * runtime pass: the dialog vanished, the row stayed Active, and nothing said why. The toast now
   * explains it, and leaving the dialog up keeps the explanation attached to the row it is about.
   */
  async confirmDeactivation(): Promise<void> {
    const user = this.confirmDeactivate();
    if (!user) return;
    if (await this.store.deactivate(user.userId)) this.confirmDeactivate.set(null);
  }

  askRemove(user: TenantUser): void {
    this.rowMenu.close();
    this.confirmRemove.set(user);
  }

  async confirmRemoval(): Promise<void> {
    const user = this.confirmRemove();
    if (!user) return;
    if (await this.store.remove(user.userId)) this.confirmRemove.set(null);
  }

  async confirmRevocation(): Promise<void> {
    const invitation = this.confirmRevoke();
    if (!invitation) return;
    if (await this.store.revoke(invitation.id)) this.confirmRevoke.set(null);
  }

  async reactivate(user: TenantUser): Promise<void> {
    this.rowMenu.close();
    await this.store.reactivate(user.userId);
  }

  async resend(invitation: Invitation): Promise<void> {
    this.rowMenu.close();
    await this.store.resend(invitation.id);
  }
}
