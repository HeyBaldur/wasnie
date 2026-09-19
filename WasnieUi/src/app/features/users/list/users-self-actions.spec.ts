import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { UsersListComponent } from './users-list.component';
import { UsersStore } from '../state/users.store';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { TenantUser } from '../models/user.model';

const ME = 'user-me';
const SOMEBODY_ELSE = 'user-other';

function user(userId: string, over: Partial<TenantUser> = {}): TenantUser {
  return {
    userId,
    email: `${userId}@acme.com`,
    firstName: 'Ana',
    lastName: 'García',
    role: 'TenantAdmin',
    isActive: true,
    emailConfirmed: true,
    createdAt: '2026-01-01T00:00:00Z',
    deactivatedAt: null,
    invitedByEmail: null,
    linkedPayeeId: null,
    linkedPayeeName: null,
    ...over,
  };
}

/**
 * KAN-93, Bugs 2 and 3, asserted where they are actually visible.
 *
 * ★★ THROUGH THE DOM, BECAUSE THE DEFECT WAS ONLY EVER IN THE DOM (§A3). The backend has refused an
 * administrator acting on themselves since KAN-32 and has counted admins for just as long; what was
 * broken is that the menu offered the actions anyway, so the only way to learn the rule was to press a
 * button and read a toast. A test on `isSelf()` would pass just as happily with the template still
 * rendering all three items — which is the exact failure this repo keeps meeting.
 *
 * ★★ THE ACTIONS MOVED FROM THE ⋮ INTO THE ACCESS PANEL, AND THESE TESTS MOVED WITH THEM. The rules
 * are unchanged — all three authority actions hidden on your own row, the payee link offered on every
 * row — but they are now enforced in `user-access-panel`, reached by selecting the row.
 *
 * ★★ THE OLD HARNESS IS GONE, AND SO ARE ITS TWO TRAPS. Opening the ⋮ needed a hand-built event with
 * `currentTarget` defined (the controller measured the dropdown from it) and the fixture pinned to the
 * top of the viewport (`RowMenuController` closes a menu whose trigger has scrolled out of sight, and
 * Karma leaves earlier fixtures in the document). Selecting a row needs neither: it sets a signal.
 *
 * ★ RETARGETED RATHER THAN DELETED. Left pointing at `.row-menu__item`, every one of these would have
 * gone green the moment the menu was removed — finding no forbidden buttons because it found no
 * buttons at all. That is the dead green this repo has a rule about, and the `withContext` guard below
 * is what stops it: the panel has to be on screen before anything is asserted about its absence.
 */
describe('UsersListComponent — what a row offers', () => {
  let fixture: ComponentFixture<UsersListComponent>;
  let store: Partial<UsersStore>;

  /** The users the current mount was given, in the order the table renders them. */
  let rendered: TenantUser[] = [];

  function mountWith(users: TenantUser[]): void {
    rendered = users;
    (store.users as unknown as jasmine.Spy).and.returnValue(users);
    fixture = TestBed.createComponent(UsersListComponent);
    fixture.detectChanges();
  }

  /**
   * Selects a row, which is what opens the panel.
   *
   * ★ THE GUARD IS THE WHOLE POINT. Without it, a panel that failed to render would make every
   * "does not offer X" assertion below pass for the wrong reason.
   */
  function openPanelFor(userId: string): HTMLElement {
    const row = rendered.findIndex(u => u.userId === userId);
    const tr = (fixture.nativeElement as HTMLElement)
      .querySelectorAll<HTMLTableRowElement>('tbody tr.users__row')[row];

    expect(tr).withContext(`no row rendered for ${userId}`).toBeTruthy();

    tr.click();
    fixture.detectChanges();

    const panel = (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLElement>('app-user-access-panel');

    expect(panel).withContext(`no access panel opened for ${userId}`).toBeTruthy();

    return panel!;
  }

  /** The labels the panel's action strip offers. */
  function menuLabelsFor(userId: string): string[] {
    const panel = openPanelFor(userId);

    return Array.from(panel.querySelectorAll('.access-panel__actions ws-button'))
      .map(b => (b.textContent ?? '').trim());
  }

  beforeEach(async () => {
    store = {
      users: jasmine.createSpy('users').and.returnValue([]),
      seats: jasmine.createSpy('seats').and.returnValue(null),
      pendingInvitations: jasmine.createSpy('pendingInvitations').and.returnValue([]),
      closedInvitations: jasmine.createSpy('closedInvitations').and.returnValue([]),
      canInvite: jasmine.createSpy('canInvite').and.returnValue(true),
      showsSeatCounter: jasmine.createSpy('showsSeatCounter').and.returnValue(false),
      saving: jasmine.createSpy('saving').and.returnValue(false),
      load: jasmine.createSpy('load').and.returnValue(Promise.resolve()),
      // The access panel reads both. `rolesLoaded` true with a populated set, so the capability list
      // renders — these tests are about the ACTION strip, and a panel stuck on "unknown" would still
      // draw it, but a double that lies about the shape is how a spec stops resembling the product.
      rolesLoaded: jasmine.createSpy('rolesLoaded').and.returnValue(true),
      roleOptions: jasmine.createSpy('roleOptions').and.returnValue([]),
      permissionsFor: jasmine.createSpy('permissionsFor')
        .and.returnValue(new Set(['Users.Manage', 'LedgerSummary.Read'])),
    } as unknown as Partial<UsersStore>;

    const currentUser = {
      currentUser: () => ({ userId: ME }),
      // Every action on this screen is gated on Users.Manage; the reader here is an administrator,
      // so the question under test is what they are offered, not whether they are offered anything.
      hasPermission: () => true,
    };

    await TestBed.configureTestingModule({
      imports: [UsersListComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: UsersStore, useValue: store },
        { provide: CurrentUserService, useValue: currentUser },
      ],
    }).compileComponents();
  });

  // ── Bug 3: nothing that touches your own authority ──────────────────────────

  it('offers no role change, deactivation or removal on your own row', () => {
    mountWith([user(ME)]);

    const labels = menuLabelsFor(ME);

    expect(labels).not.toContain('USERS.ACTION.CHANGE_ROLE');
    expect(labels).not.toContain('USERS.ACTION.DEACTIVATE');
    expect(labels).not.toContain('USERS.ACTION.REMOVE');
  });

  it('explains the absence instead of leaving a gap where the actions were', () => {
    mountWith([user(ME)]);

    const panel = openPanelFor(ME);

    expect(panel.querySelector('.access-panel__self-note')?.textContent)
      .toContain('USERS.ACTION.SELF_NOTE');
  });

  it('still offers all three on somebody else', () => {
    mountWith([user(ME), user(SOMEBODY_ELSE)]);

    const labels = menuLabelsFor(SOMEBODY_ELSE);

    expect(labels).toContain('USERS.ACTION.CHANGE_ROLE');
    expect(labels).toContain('USERS.ACTION.DEACTIVATE');
    expect(labels).toContain('USERS.ACTION.REMOVE');
  });

  // ── Bug 2: the payee link ───────────────────────────────────────────────────

  /**
   * ★★ OFFERED ON YOUR OWN ROW TOO, and deliberately. Attaching yourself to your own payee record is
   * the ordinary case for a founding administrator who also sells. It changes what you may READ, not
   * what you may DO, so the self-action rule above does not reach it.
   */
  it('offers the payee link on your own row as well', () => {
    mountWith([user(ME)]);

    expect(menuLabelsFor(ME)).toContain('USERS.ACTION.LINK_PAYEE');
  });

  it('offers unlinking, not linking, once a payee is attached', () => {
    mountWith([user(SOMEBODY_ELSE, { linkedPayeeId: 'p-1', linkedPayeeName: 'Ana García' })]);

    const labels = menuLabelsFor(SOMEBODY_ELSE);

    expect(labels).toContain('USERS.ACTION.UNLINK_PAYEE');
    expect(labels).not.toContain('USERS.ACTION.LINK_PAYEE');
  });

  /**
   * ★★ "NOT LINKED" IS SAID OUT LOUD (§C3). An empty cell reads as "nothing to report"; this is the
   * state that silently breaks somebody's personal dashboard, and it is the only thing on this screen
   * an administrator has to act on. Before KAN-93 the column did not exist at all, so a stranded user
   * was indistinguishable from a working one.
   */
  it('names the unlinked state rather than leaving the cell blank', () => {
    mountWith([user(SOMEBODY_ELSE)]);

    const row = (fixture.nativeElement as HTMLElement).querySelector('tbody tr');

    expect(row?.textContent).toContain('USERS.PAYEE.NOT_LINKED');
  });

  it('shows the payee name when one is attached', () => {
    mountWith([user(SOMEBODY_ELSE, { linkedPayeeId: 'p-1', linkedPayeeName: 'Ana García' })]);

    const cell = (fixture.nativeElement as HTMLElement).querySelector('.users__payee');

    expect(cell?.textContent?.trim()).toBe('Ana García');
    expect((fixture.nativeElement as HTMLElement).textContent)
      .not.toContain('USERS.PAYEE.NOT_LINKED');
  });
});
