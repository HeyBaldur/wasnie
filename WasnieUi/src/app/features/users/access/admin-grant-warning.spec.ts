import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { UsersListComponent } from '../list/users-list.component';
import { InviteUserFormComponent } from '../invite/invite-user-form.component';
import { UsersStore } from '../state/users.store';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { TenantRole, TenantUser } from '../models/user.model';

function user(role: TenantRole): TenantUser {
  return {
    userId: 'user-other',
    email: 'other@acme.com',
    firstName: 'Ana',
    lastName: 'García',
    role,
    isActive: true,
    emailConfirmed: true,
    createdAt: '2026-01-01T00:00:00Z',
    deactivatedAt: null,
    invitedByEmail: null,
    linkedPayeeId: null,
    linkedPayeeName: null,
  };
}

/**
 * Handing the administrator role to somebody else is warned about in the two places it can happen.
 *
 * ★★ ASSERTED IN THE DOM, BOTH WAYS (§A2/§A3). A test that only checked the warning appears would
 * stay green if it appeared for EVERY role — which is the version of this feature that teaches people
 * to ignore it. So each place is checked for the role that must warn and for the ones that must not.
 *
 * ★ SEARCHED IN `document`, not only in the fixture: `ws-modal` may render its body elsewhere.
 */
describe('Admin grant warning', () => {
  const store = {
    users: () => [],
    seats: () => null,
    pendingInvitations: () => [],
    closedInvitations: () => [],
    canInvite: () => true,
    showsSeatCounter: () => false,
    saving: () => false,
    rolesLoaded: () => true,
    permissionsFor: () => new Set<string>(),
    roleOptions: () => [
      { value: 'TenantAdmin', label: 'USERS.ROLES.TENANT_ADMIN' },
      { value: 'Rep', label: 'USERS.ROLES.REP' },
    ],
    load: () => Promise.resolve(),
    loadUnlinkedPayees: () => Promise.resolve({ items: [], total: 0 }),
  } as unknown as UsersStore;

  /**
   * Live warnings only. ★ A closing `ws-modal` leaves an exit copy (`.ws-modal--leaving`) in
   * `document.body` for its 220 ms animation, and Karma runs specs in random order: a "does not warn"
   * test that ran right after a "warns" one counted the previous test's ghost. Excluded here, and
   * swept before each test the same way `ws-modal-exit-styles.spec.ts` does.
   */
  const warnings = (): number =>
    Array.from(document.querySelectorAll('app-admin-grant-warning'))
      .filter(w => !w.closest('.ws-modal--leaving')).length;

  beforeEach(async () => {
    document.body.querySelectorAll('.ws-modal--leaving').forEach(n => n.remove());
    await TestBed.configureTestingModule({
      imports: [UsersListComponent, InviteUserFormComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: UsersStore, useValue: store },
        { provide: CurrentUserService, useValue: { currentUser: () => ({ userId: 'user-me' }), hasPermission: () => true } },
      ],
    }).compileComponents();
  });

  // And after: `fixture.destroy()` on an open modal is what CREATES the ghost, and another spec that
  // looks for its own ghost (ws-modal-exit-styles) must not find ours instead.
  afterEach(() => document.body.querySelectorAll('.ws-modal--leaving').forEach(n => n.remove()));

  describe('changing a role', () => {
    function openFor(target: TenantUser, picked: TenantRole) {
      const fixture = TestBed.createComponent(UsersListComponent);
      fixture.detectChanges();
      fixture.componentInstance.openRoleChange(target);
      fixture.componentInstance.roleForm.controls.role.setValue(picked);
      fixture.detectChanges();
      return fixture;
    }

    it('warns when a Sales rep is about to become an administrator', () => {
      const fixture = openFor(user('Rep'), 'TenantAdmin');

      expect(warnings()).toBe(1);
      // Named, so the admin reads WHO gets the keys, not an abstract sentence.
      expect(document.querySelector('app-admin-grant-warning:not(.ws-modal--leaving *)')?.textContent)
        .toContain('USERS.ADMIN_WARNING.LEAD_NAMED');
      fixture.destroy();
    });

    it('does not warn for a non-admin role', () => {
      const fixture = openFor(user('TenantAdmin'), 'Rep');

      expect(warnings()).toBe(0);
      fixture.destroy();
    });

    it('does not warn when an existing administrator is re-saved as one — nothing is granted', () => {
      const fixture = openFor(user('TenantAdmin'), 'TenantAdmin');

      expect(warnings()).toBe(0);
      fixture.destroy();
    });
  });

  describe('inviting', () => {
    it('warns when inviting straight in as an administrator, and stops when the role changes', () => {
      const fixture = TestBed.createComponent(InviteUserFormComponent);
      fixture.detectChanges();
      const role = fixture.componentInstance.form.controls.role;

      role.setValue('TenantAdmin');
      fixture.detectChanges();
      expect(warnings()).toBe(1);
      expect(document.querySelector('app-admin-grant-warning')?.textContent)
        .toContain('USERS.ADMIN_WARNING.LEAD_INVITE');

      role.setValue('Rep');
      fixture.detectChanges();
      expect(warnings()).toBe(0);
      fixture.destroy();
    });
  });
});
