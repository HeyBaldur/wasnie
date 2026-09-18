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
 * ★★ OPENING THE MENU NEEDS THE REAL TRIGGER ELEMENT, AND TWO TRAPS SIT ON THE WAY THERE.
 * `RowMenuController.toggle` measures where to pin the dropdown from `event.currentTarget`, which the
 * browser only populates while an event is being dispatched — a hand-built `MouseEvent` leaves it
 * null and the template then throws on `menuPosition()!.top`. But dispatching a real `.click()` is
 * not the answer either: the component also closes the menu from a `document:click` host listener,
 * and in the test harness that listener won the race often enough to make the suite flap between
 * runs. So the event is dispatched at the trigger with `currentTarget` supplied explicitly: the
 * controller reads exactly what a browser would give it, and nothing else is listening.
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

    // ★★ PINNED TO THE TOP OF THE VIEWPORT, AND WITHOUT THIS THE SUITE FLAPS. `RowMenuController`
    // CLOSES a menu whose trigger has scrolled out of sight (`rect.top > window.innerHeight`) — right,
    // in the product, and fatal here: Karma leaves every earlier spec's fixture in the document, so
    // this table renders further down the page the later this file happens to run. The menu then
    // closed the instant it opened, the dropdown query came back empty, and WHICH tests failed changed
    // with the randomised order. The component is not at fault; the harness is, so the harness is what
    // is fixed.
    const host = fixture.nativeElement as HTMLElement;
    host.style.position = 'fixed';
    host.style.top = '0';
    host.style.left = '0';

    fixture.detectChanges();
  }

  /** Opens a row's ⋮ menu — see the note above for why it is done this way. */
  function openMenuFor(userId: string): void {
    const row = rendered.findIndex(u => u.userId === userId);
    const trigger = (fixture.nativeElement as HTMLElement)
      .querySelectorAll<HTMLButtonElement>('tbody .row-menu__trigger')[row];

    expect(trigger).withContext(`no ⋮ trigger rendered for ${userId}`).toBeTruthy();

    const event = new MouseEvent('click');
    Object.defineProperty(event, 'currentTarget', { value: trigger });
    fixture.componentInstance.toggleMenu(userId, event);
    fixture.detectChanges();
  }

  /** The labels the open menu offers. */
  function menuLabelsFor(userId: string): string[] {
    openMenuFor(userId);

    const el = fixture.nativeElement as HTMLElement;
    return Array.from(el.querySelectorAll('.row-menu__dropdown .row-menu__item'))
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

  it('explains the absence instead of showing an empty menu', () => {
    mountWith([user(ME)]);

    openMenuFor(ME);

    const note = (fixture.nativeElement as HTMLElement)
      .querySelector('.row-menu__dropdown .row-menu__note');

    expect(note?.textContent).toContain('USERS.ACTION.SELF_NOTE');
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
