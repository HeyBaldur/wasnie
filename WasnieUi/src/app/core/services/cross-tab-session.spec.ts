import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { InactivityService } from './inactivity.service';
import { AuthService } from './auth.service';
import { TabSyncService, TabSyncMessage } from './tab-sync.service';
import { ToastService } from '../../shared/services/toast.service';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function makeTabSyncMock(): {
  messages$: Subject<TabSyncMessage>;
  broadcast: jasmine.Spy;
  service: TabSyncService;
} {
  const messages$ = new Subject<TabSyncMessage>();
  const broadcast = jasmine.createSpy('broadcast');
  return {
    messages$,
    broadcast,
    service: { messages$: messages$.asObservable(), broadcast, ngOnDestroy: () => {} } as unknown as TabSyncService,
  };
}

function makeAuthSpy(): jasmine.SpyObj<AuthService> {
  const spy = jasmine.createSpyObj<AuthService>('AuthService', [
    'clearSessionSilent', 'forceLogout', 'refresh', 'logout',
  ]);
  (spy as any).isAuthenticated = () => true;
  return spy;
}

// ─── InactivityService — sync cross-tab behaviors (no timer advancement needed)
// These tests verify synchronous reactions to tab-sync messages.
// start() is called in beforeEach with a real (not fake) setTimeout — cleared in afterEach.
// ─────────────────────────────────────────────────────────────────────────────
describe('InactivityService — cross-tab sync reactions', () => {
  let inactivity: InactivityService;
  let tabSyncMock: ReturnType<typeof makeTabSyncMock>;
  let authSpy: jasmine.SpyObj<AuthService>;
  let routerSpy: jasmine.SpyObj<Router>;
  let toastSpy: jasmine.SpyObj<ToastService>;

  beforeEach(() => {
    tabSyncMock = makeTabSyncMock();
    authSpy = makeAuthSpy();
    routerSpy = jasmine.createSpyObj<Router>('Router', ['navigateByUrl']);
    toastSpy = jasmine.createSpyObj<ToastService>('ToastService', ['show']);

    TestBed.configureTestingModule({
      providers: [
        InactivityService,
        { provide: TabSyncService, useValue: tabSyncMock.service },
        { provide: AuthService,    useValue: authSpy },
        { provide: Router,         useValue: routerSpy },
        { provide: ToastService,   useValue: toastSpy },
      ],
    });

    inactivity = TestBed.inject(InactivityService);
    inactivity.start(); // creates a real 28-min setTimeout (cleared in afterEach)
  });

  afterEach(() => {
    inactivity.stop(); // clears the real 28-min timer
  });

  it('receiving "logout" calls clearSessionSilent (not logout) and redirects without toast', () => {
    tabSyncMock.messages$.next({ type: 'logout' });

    expect(authSpy.clearSessionSilent).toHaveBeenCalledTimes(1);
    expect(authSpy.logout).not.toHaveBeenCalled();
    expect(toastSpy.show).not.toHaveBeenCalled();
    expect(routerSpy.navigateByUrl).toHaveBeenCalledOnceWith('/auth/login');
  });

  it('receiving "session-expired" calls clearSessionSilent and shows the expiry toast', () => {
    tabSyncMock.messages$.next({ type: 'session-expired' });

    expect(authSpy.clearSessionSilent).toHaveBeenCalledTimes(1);
    expect(authSpy.logout).not.toHaveBeenCalled();
    expect(toastSpy.show).toHaveBeenCalledOnceWith('SESSION.EXPIRED_TOAST', 'error');
    expect(routerSpy.navigateByUrl).toHaveBeenCalledOnceWith('/auth/login');
  });

  it('receiving "logout" does NOT re-broadcast to the channel', () => {
    tabSyncMock.messages$.next({ type: 'logout' });
    expect(tabSyncMock.broadcast).not.toHaveBeenCalled();
  });

  it('receiving "session-expired" does NOT re-broadcast to the channel', () => {
    tabSyncMock.messages$.next({ type: 'session-expired' });
    expect(tabSyncMock.broadcast).not.toHaveBeenCalled();
  });

  it('stop() unsubscribes from the channel so subsequent messages are ignored', () => {
    inactivity.stop(); // double-stop is ok because stop() guards on running
    tabSyncMock.messages$.next({ type: 'logout' });

    expect(authSpy.clearSessionSilent).not.toHaveBeenCalled();
    expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
  });
});

// ─── InactivityService — cross-tab timer reactions (fakeAsync, start inside it)
// Timer-based tests: start() is called INSIDE each fakeAsync block so tick() advances its timer.
// ─────────────────────────────────────────────────────────────────────────────
describe('InactivityService — cross-tab timer reactions', () => {
  let inactivity: InactivityService;
  let tabSyncMock: ReturnType<typeof makeTabSyncMock>;
  let authSpy: jasmine.SpyObj<AuthService>;

  beforeEach(() => {
    tabSyncMock = makeTabSyncMock();
    authSpy = makeAuthSpy();

    TestBed.configureTestingModule({
      providers: [
        InactivityService,
        { provide: TabSyncService, useValue: tabSyncMock.service },
        { provide: AuthService,    useValue: authSpy },
        { provide: Router,         useValue: jasmine.createSpyObj('Router', ['navigateByUrl']) },
        { provide: ToastService,   useValue: jasmine.createSpyObj('ToastService', ['show']) },
      ],
    });

    inactivity = TestBed.inject(InactivityService);
    // NOTE: start() is called INSIDE each fakeAsync it block, not here.
  });

  it('receiving "activity" when idle resets the idle timer (warning does not open)', fakeAsync(() => {
    inactivity.start();

    tick(20 * 60 * 1000); // 20 min — not yet at 28-min threshold

    tabSyncMock.messages$.next({ type: 'activity' }); // remote activity resets all timers

    tick(10 * 60 * 1000); // 10 more min — would have passed 28 min from original start, but not from reset
    expect(inactivity.warningOpen()).toBeFalse();

    inactivity.stop(); // clears pending 28-min timer so fakeAsync doesn't complain
  }));

  it('receiving "activity" when warning is open dismisses it and restarts the idle timer', fakeAsync(() => {
    inactivity.start();

    tick(28 * 60 * 1000); // trigger idle → warning opens
    expect(inactivity.warningOpen()).toBeTrue();

    tabSyncMock.messages$.next({ type: 'activity' }); // another tab active → dismiss warning

    expect(inactivity.warningOpen()).toBeFalse();

    // The countdown interval was cleared — expiry should NOT fire even after 2 min
    tick(2 * 60 * 1000 + 1000);
    expect(authSpy.forceLogout).not.toHaveBeenCalled();

    inactivity.stop();
  }));
});

// ─── InactivityService — activity broadcast throttle ──────────────────────────
// start() and stop() are managed inline in each test (no fakeAsync needed — throttle uses Date.now).
// ─────────────────────────────────────────────────────────────────────────────
describe('InactivityService — activity broadcast throttle', () => {
  let inactivity: InactivityService;
  let tabSyncMock: ReturnType<typeof makeTabSyncMock>;

  beforeEach(() => {
    tabSyncMock = makeTabSyncMock();

    TestBed.configureTestingModule({
      providers: [
        InactivityService,
        { provide: TabSyncService, useValue: tabSyncMock.service },
        { provide: AuthService,    useValue: makeAuthSpy() },
        { provide: Router,         useValue: jasmine.createSpyObj('Router', ['navigateByUrl']) },
        { provide: ToastService,   useValue: jasmine.createSpyObj('ToastService', ['show']) },
      ],
    });

    inactivity = TestBed.inject(InactivityService);
    inactivity.start(); // uses real setTimeout; cleared in afterEach
  });

  afterEach(() => {
    inactivity.stop();
  });

  it('first user activity broadcasts "activity" to the channel', () => {
    (inactivity as any).resetTimer();
    expect(tabSyncMock.broadcast).toHaveBeenCalledOnceWith({ type: 'activity' });
  });

  it('rapid repeated activity does not flood the channel (throttled)', () => {
    for (let i = 0; i < 10; i++) {
      (inactivity as any).resetTimer();
    }
    // Date.now() barely advances during synchronous calls → throttle condition prevents
    // subsequent broadcasts. At most 1 broadcast in a rapid burst.
    expect(tabSyncMock.broadcast.calls.count()).toBeLessThanOrEqual(1);
    expect(tabSyncMock.broadcast.calls.count()).toBeGreaterThanOrEqual(1);
  });

  it('allows a second broadcast after the throttle interval has elapsed', () => {
    (inactivity as any).resetTimer(); // first broadcast
    // Simulate time passing by rewinding _lastActivityBroadcast
    (inactivity as any)._lastActivityBroadcast = Date.now() - 5_001;
    (inactivity as any).resetTimer(); // second broadcast
    expect(tabSyncMock.broadcast).toHaveBeenCalledTimes(2);
  });

  it('does not broadcast when the warning modal is open', () => {
    // Simulate warning already open
    inactivity.warningOpen.set(true);
    tabSyncMock.broadcast.calls.reset();

    (inactivity as any).resetTimer(); // returns early because warningOpen()
    expect(tabSyncMock.broadcast).not.toHaveBeenCalled();
  });
});

// ─── AuthService — cross-tab broadcast ───────────────────────────────────────
describe('AuthService — cross-tab broadcast', () => {
  let auth: AuthService;
  let tabSyncMock: ReturnType<typeof makeTabSyncMock>;

  beforeEach(() => {
    tabSyncMock = makeTabSyncMock();

    spyOn(localStorage, 'removeItem');
    spyOn(localStorage, 'setItem');
    spyOn(localStorage, 'getItem').and.returnValue(
      JSON.stringify({
        userId: 'u1', email: 'a@b.com', tenantId: 't1', tenantSlug: 'slug',
        roles: ['TenantAdmin'],
        tokens: {
          accessToken: 'at', refreshToken: 'rt',
          accessTokenExpiresAt: '', refreshTokenExpiresAt: '',
        },
      }),
    );

    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        AuthService,
        { provide: TabSyncService, useValue: tabSyncMock.service },
      ],
    });

    auth = TestBed.inject(AuthService);
  });

  it('logout() broadcasts "logout" to other tabs', () => {
    auth.logout();
    expect(tabSyncMock.broadcast).toHaveBeenCalledOnceWith({ type: 'logout' });
  });

  it('forceLogout() broadcasts "session-expired" to other tabs', () => {
    auth.forceLogout(false);
    expect(tabSyncMock.broadcast).toHaveBeenCalledOnceWith({ type: 'session-expired' });
  });

  it('clearSessionSilent() does NOT broadcast anything', () => {
    auth.clearSessionSilent();
    expect(tabSyncMock.broadcast).not.toHaveBeenCalled();
  });

  it('logout() does not broadcast if session was already cleared', () => {
    auth.clearSessionSilent(); // isAuthenticated() → false
    tabSyncMock.broadcast.calls.reset();

    auth.logout();
    expect(tabSyncMock.broadcast).not.toHaveBeenCalled();
  });

  it('clearSessionSilent() clears isAuthenticated and removes the localStorage entry', () => {
    expect(auth.isAuthenticated()).toBeTrue();

    auth.clearSessionSilent();

    expect(auth.isAuthenticated()).toBeFalse();
    expect(localStorage.removeItem).toHaveBeenCalledWith('wasnie_session');
  });
});
