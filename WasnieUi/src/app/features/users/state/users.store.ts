import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError, extractApiErrorCode } from '../../../shared/utils/api-error';
import { UsersApiService } from '../services/users.api.service';
import {
  Invitation,
  RolePermissions,
  SeatUsage,
  TenantRole,
  TenantUser,
  TenantUsersResponse,
  UnlinkedPayeesResponse,
} from '../models/user.model';

/**
 * KAN-32 — the users screen's state.
 *
 * ★★ THERE IS NO PAGINATION AND NO SERVER-SIDE SEARCH HERE, unlike every other list store in this
 * app. The population is the staff of one company using one tool: tens of rows, and it does not grow
 * with the customer's revenue. The backend handler carries the same note. If a tenant ever turns up
 * with hundreds of logins, this is the comment that says the decision was made with a number in mind.
 *
 * ★ EVERY MUTATION RELOADS THE WHOLE RESPONSE. Seats, users and invitations are computed together on
 * the server against one clock; patching one of them locally after a revoke would leave the seat
 * counter describing a world that no longer exists.
 */
@Injectable({ providedIn: 'root' })
export class UsersStore {
  private readonly api = inject(UsersApiService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  private readonly response = signal<TenantUsersResponse | null>(null);

  readonly users = computed<TenantUser[]>(() => this.response()?.users ?? []);
  readonly seats = computed<SeatUsage | null>(() => this.response()?.seats ?? null);

  /**
   * Only what is still outstanding.
   *
   * ★ Accepted invitations are deliberately NOT listed: the person is in the users table above, and
   * showing them twice would make a two-person tenant look like four rows.
   */
  readonly pendingInvitations = computed<Invitation[]>(() =>
    (this.response()?.invitations ?? []).filter((i) => i.status === 'Pending'),
  );

  /** Invitations that ran out or were withdrawn — history, collapsed by default on screen. */
  readonly closedInvitations = computed<Invitation[]>(() =>
    (this.response()?.invitations ?? []).filter(
      (i) => i.status === 'Expired' || i.status === 'Revoked',
    ),
  );

  /**
   * What each role may do, from the server.
   *
   * ★★ THE ACCESS PANEL'S ONLY SOURCE. The alternative was a permission map written in TypeScript,
   * which would go stale silently — on the one screen whose job is telling an administrator who can do
   * what. See `users.api.service.rolePermissions()`.
   *
   * ★ AN EMPTY MAP IS "NOT LOADED", NOT "NO PERMISSIONS". The panel shows a loading state for it
   * rather than a page of red crosses: telling an administrator that somebody can do nothing, because
   * a request has not landed, is the false zero wearing a different hat.
   */
  private readonly roles = signal<RolePermissions[]>([]);

  readonly rolesLoaded = computed(() => this.roles().length > 0);

  /**
   * The permission keys a role holds. Unknown role, or nothing loaded yet → an empty set, which the
   * panel renders as "unknown" rather than as "denied" (see `rolesLoaded`).
   */
  permissionsFor(role: TenantRole | null): ReadonlySet<string> {
    if (!role) return new Set<string>();
    const found = this.roles().find((r) => r.role === role);
    return new Set(found?.permissions ?? []);
  }

  readonly activeUsers = computed(() => this.users().filter((u) => u.isActive));
  readonly deactivatedUsers = computed(() => this.users().filter((u) => !u.isActive));

  /** Whether the invite button may be pressed at all. Derived from the server's answer, never local. */
  readonly canInvite = computed(() => this.seats()?.hasRoom ?? true);

  /** A seat counter is only meaningful when there is a limit; today there never is. */
  readonly showsSeatCounter = computed(() => this.seats()?.limit != null);

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.response.set(await firstValueFrom(this.api.getUsers()));
    } catch {
      this.error.set('USERS_LOAD_FAILED');
    } finally {
      this.loading.set(false);
    }

    void this.loadRoles();
  }

  /**
   * ★★ FETCHED SEPARATELY AND NOT AWAITED WITH THE LIST, on purpose. The table is what the screen is
   * for; making its first paint wait on a map that only the side panel reads would slow down every
   * visit for a panel most of them never open.
   *
   * ★ AND ONCE PER PAGE. It is a constant of the product — it cannot change while somebody is looking
   * at the screen — so re-fetching it after every mutation would be a request that can only ever
   * return the same bytes. A failure leaves it empty, which the panel reads as "unknown".
   */
  private async loadRoles(): Promise<void> {
    if (this.roles().length > 0) return;

    try {
      this.roles.set(await firstValueFrom(this.api.rolePermissions()));
    } catch {
      // Left empty on purpose: the panel says it does not know, rather than saying "nothing".
    }
  }

  async invite(email: string, role: TenantRole, payeeId: string | null): Promise<boolean> {
    return this.mutate(() => firstValueFrom(this.api.invite({ email, role, payeeId })));
  }

  /**
   * KAN-92. The payees the invite form may attach somebody to.
   *
   * ★ IT DOES NOT GO THROUGH `mutate`. Failing to load the picker must not raise the refusal toast:
   * the admin can still invite without attaching a payee, and an error here is not a refusal of
   * anything they asked for.
   */
  async loadUnlinkedPayees(emailHint?: string, search?: string): Promise<UnlinkedPayeesResponse | null> {
    try {
      return await firstValueFrom(this.api.unlinkedPayees(emailHint, search));
    } catch {
      return null;
    }
  }

  async resend(invitationId: string): Promise<boolean> {
    return this.mutate(() => firstValueFrom(this.api.resendInvitation(invitationId)));
  }

  async revoke(invitationId: string): Promise<boolean> {
    return this.mutate(() => firstValueFrom(this.api.revokeInvitation(invitationId)));
  }

  async deactivate(userId: string): Promise<boolean> {
    return this.mutate(() => firstValueFrom(this.api.deactivate(userId)));
  }

  async reactivate(userId: string): Promise<boolean> {
    return this.mutate(() => firstValueFrom(this.api.reactivate(userId)));
  }

  async remove(userId: string): Promise<boolean> {
    return this.mutate(() => firstValueFrom(this.api.remove(userId)));
  }

  async changeRole(userId: string, role: TenantRole): Promise<boolean> {
    return this.mutate(() => firstValueFrom(this.api.changeRole(userId, role)));
  }

  /**
   * KAN-93. Attaches this login to a payee record, or detaches it with null.
   *
   * ★ IT GOES THROUGH `mutate` AND THEREFORE RELOADS. The linked payee is now a column on this
   * screen; patching it locally would leave the row describing a link the server may have refused.
   */
  async linkPayee(userId: string, payeeId: string | null): Promise<boolean> {
    return this.mutate(() => firstValueFrom(this.api.linkPayee(userId, payeeId)));
  }

  /**
   * One shape for all six mutations: run it, SAY SOMETHING IF IT FAILED, reload, report.
   *
   * ★★ THE TOAST IS RAISED HERE AND THAT WAS A DEFECT FOUND IN RUNTIME, NOT A DESIGN. The first cut
   * caught the error and returned false with a comment claiming "the interceptor has already
   * surfaced the coded refusal". It has not: `error.interceptor` handles 401 and nothing else.
   * Confirmed on screen — refusing to deactivate the last administrator closed the dialog and said
   * NOTHING, which is §B1 exactly: a system that cannot do something has to leave something a person
   * can see.
   *
   * ★ THE CODE GOES THROUGH A WHITELIST, NEVER STRAIGHT TO THE SCREEN. `refusalKey` maps the ones
   * this build knows; anything else falls back to the server sentence and then to a generic line, so
   * an unrecognised identifier is never painted at a user (§C2).
   */
  private async mutate(action: () => Promise<unknown>): Promise<boolean> {
    this.saving.set(true);
    try {
      await action();
      await this.load();
      return true;
    } catch (err: unknown) {
      const coded = extractApiErrorCode(err);
      this.toast.show(
        coded ? this.refusalKey(coded.code) : extractApiError(err),
        'error',
        coded?.parameters);
      return false;
    } finally {
      this.saving.set(false);
    }
  }

  /**
   * The translation key for a refusal code.
   *
   * ★ AN EXPLICIT LIST (§C2). `'USERS.REFUSAL.' + code` would print an internal identifier the day
   * the backend adds a code this build has never heard of.
   */
  private refusalKey(code: string): string {
    switch (code) {
      case 'INVITATION_EMAIL_ALREADY_MEMBER': return 'USERS.REFUSAL.ALREADY_MEMBER';
      case 'INVITATION_EMAIL_ALREADY_INVITED': return 'USERS.REFUSAL.ALREADY_INVITED';
      case 'INVITATION_NO_SEATS_AVAILABLE': return 'USERS.REFUSAL.NO_SEATS';
      case 'INVITATION_ROLE_UNKNOWN': return 'USERS.REFUSAL.ROLE_UNKNOWN';
      case 'INVITATION_PAYEE_NOT_FOUND': return 'USERS.REFUSAL.PAYEE_NOT_FOUND';
      case 'INVITATION_PAYEE_ALREADY_LINKED': return 'USERS.REFUSAL.PAYEE_ALREADY_LINKED';
      case 'INVITATION_TOKEN_ALREADY_USED': return 'USERS.REFUSAL.ALREADY_USED';
      case 'INVITATION_TOKEN_EXPIRED': return 'USERS.REFUSAL.EXPIRED';
      case 'INVITATION_TOKEN_REVOKED': return 'USERS.REFUSAL.REVOKED';
      case 'USER_LAST_ADMIN': return 'USERS.REFUSAL.LAST_ADMIN';
      case 'USER_CANNOT_ACT_ON_SELF': return 'USERS.REFUSAL.CANNOT_ACT_ON_SELF';
      default: return 'ERRORS.GENERIC';
    }
  }
}
