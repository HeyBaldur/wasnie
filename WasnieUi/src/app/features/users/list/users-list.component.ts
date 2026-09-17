import { Component, HostListener, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { createRowMenu } from '../../../shared/utils/row-menu';
import { UsersStore } from '../state/users.store';
import { Invitation, InvitationStatus, TenantRole, TenantUser } from '../models/user.model';
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

  ngOnInit(): void {
    void this.store.load();
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
