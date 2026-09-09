import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { HttpClientTestingModule, HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { InactivityService } from './inactivity.service';
import { AuthService } from './auth.service';
import { TabSyncService, TabSyncMessage } from './tab-sync.service';
import { ToastService } from '../../shared/services/toast.service';
import { SessionExitService } from './session-exit.service';

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
  let toastSpy: jasmine.SpyObj<ToastService>;
  let exitSpy: jasmine.SpyObj<SessionExitService>;

  beforeEach(() => {
    tabSyncMock = makeTabSyncMock();
    authSpy = makeAuthSpy();
    toastSpy = jasmine.createSpyObj<ToastService>('ToastService', ['show']);
    // ★ LA SALIDA SE ESPÍA, NUNCA SE EJECUTA. En producción hace `window.location.assign`: dejarla
    // correr aquí recargaría el propio ejecutor de tests a mitad de la suite.
    exitSpy = jasmine.createSpyObj<SessionExitService>('SessionExitService', ['toLogin']);

    TestBed.configureTestingModule({
      providers: [
        InactivityService,
        { provide: TabSyncService, useValue: tabSyncMock.service },
        { provide: AuthService,        useValue: authSpy },
        { provide: SessionExitService, useValue: exitSpy },
        { provide: Router,             useValue: jasmine.createSpyObj('Router', ['navigateByUrl']) },
        { provide: ToastService,       useValue: toastSpy },
      ],
    });

    inactivity = TestBed.inject(InactivityService);
    inactivity.start(); // creates a real 28-min setTimeout (cleared in afterEach)
  });

  afterEach(() => {
    inactivity.stop(); // clears the real 28-min timer
  });

  it('receiving "logout" calls clearSessionSilent (not logout) and leaves without a notice', () => {
    tabSyncMock.messages$.next({ type: 'logout' });

    expect(authSpy.clearSessionSilent).toHaveBeenCalledTimes(1);
    expect(authSpy.logout).not.toHaveBeenCalled();
    expect(toastSpy.show).not.toHaveBeenCalled();
    expect(exitSpy.toLogin).toHaveBeenCalledOnceWith(null);
  });

  /**
   * ★ EL AVISO SE ENCARGA, NO SE PINTA. Salir recarga el documento — lo único que garantiza que no
   * sobreviva en memoria el tenant anterior — y un toast pintado aquí moriría con la recarga. Este
   * test fija esa frontera: aquí se pide `'expired'`, y quien lo muestra es la pantalla de acceso.
   */
  it('receiving "session-expired" clears the session and carries the expiry notice through the reload', () => {
    tabSyncMock.messages$.next({ type: 'session-expired' });

    expect(authSpy.clearSessionSilent).toHaveBeenCalledTimes(1);
    expect(authSpy.logout).not.toHaveBeenCalled();
    expect(toastSpy.show).not.toHaveBeenCalled();
    expect(exitSpy.toLogin).toHaveBeenCalledOnceWith('expired');
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
    expect(exitSpy.toLogin).not.toHaveBeenCalled();
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
        { provide: AuthService,        useValue: authSpy },
        { provide: SessionExitService, useValue: jasmine.createSpyObj('SessionExitService', ['toLogin']) },
        { provide: Router,             useValue: jasmine.createSpyObj('Router', ['navigateByUrl']) },
        { provide: ToastService,       useValue: jasmine.createSpyObj('ToastService', ['show']) },
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
        { provide: AuthService,        useValue: makeAuthSpy() },
        { provide: SessionExitService, useValue: jasmine.createSpyObj('SessionExitService', ['toLogin']) },
        { provide: Router,             useValue: jasmine.createSpyObj('Router', ['navigateByUrl']) },
        { provide: ToastService,       useValue: jasmine.createSpyObj('ToastService', ['show']) },
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

// ─── AuthService.refresh() — one renewal per browser (KAN-1) ──────────────────
//
// ★★ THE DEFECT THESE PIN. The server rotates the refresh token — it revokes the presented one before
// issuing the new pair — so a refresh token is single-use. Tabs deliberately never share tokens over
// BroadcastChannel, and nothing re-read `wasnie_session` when another tab wrote it. So the second tab
// spent a token the first had already used, its renewal was rejected, it called forceLogout, and the
// `session-expired` broadcast threw EVERY tab out, including the one whose session was perfectly fine.
describe('AuthService.refresh — cross-tab token rotation', () => {
  const KEY = 'wasnie_session';

  function session(accessToken: string, refreshToken: string, expiresInMs: number): string {
    return JSON.stringify({
      tenantId: 't1',
      tokens: {
        accessToken,
        refreshToken,
        accessTokenExpiresAt: new Date(Date.now() + expiresInMs).toISOString(),
        refreshTokenExpiresAt: new Date(Date.now() + 7 * 864e5).toISOString(),
      },
    });
  }

  /**
   * Waits until the renewal has actually reached the transport, then returns that request.
   *
   * ★★ POLLING, NOT A FIXED DELAY, AND THE REASON IS THE LOCK. The renewal runs inside a Web Lock —
   * a real async API that Zone.js does not patch — so its callback lands outside Angular's zone and a
   * single zone-patched turn can run BEFORE it. A fixed `setTimeout(0)` therefore asserted against a
   * request that had not been made yet and failed for a reason that had nothing to do with the code.
   * Waiting for the condition instead of guessing its duration is what makes this deterministic.
   */
  async function awaitRefreshRequest(): Promise<TestRequest> {
    for (let attempt = 0; attempt < 100; attempt++) {
      const matches = http.match(`${environment.apiBaseUrl}/auth/refresh`);
      if (matches.length === 1) return matches[0];
      await new Promise<void>(resolve => setTimeout(resolve, 5));
    }
    throw new Error('the renewal never reached the transport');
  }

  const settle = () => new Promise<void>(resolve => setTimeout(resolve, 20));

  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.removeItem(KEY);
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [{ provide: TabSyncService, useValue: makeTabSyncMock().service }],
    });
  });

  afterEach(() => localStorage.removeItem(KEY));

  /**
   * ★★ THE FIX ITSELF: a tab that finds a usable session already in storage ADOPTS it and spends
   * nothing. This is the request that used to be rejected and take every tab down with it.
   */
  it('adopts a session another tab already renewed, without calling the server', async () => {
    localStorage.setItem(KEY, session('fresh-access', 'fresh-refresh', 10 * 60_000));

    TestBed.runInInjectionContext(() => { auth = TestBed.inject(AuthService); });
    http = TestBed.inject(HttpTestingController);

    const tokens = await firstValueFrom(auth.refresh());
    await settle();

    expect(tokens.accessToken).toBe('fresh-access');
    http.expectNone(`${environment.apiBaseUrl}/auth/refresh`);
    http.verify();
  });

  /** ★ And when nothing usable is stored, it really does renew — the adoption must not swallow the call. */
  it('renews against the server when the stored access token is spent', async () => {
    localStorage.setItem(KEY, session('stale-access', 'stored-refresh', -1000));

    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);

    const pending = firstValueFrom(auth.refresh());
    const req = await awaitRefreshRequest();

    // ★ THE TOKEN SENT COMES FROM STORAGE, not from this tab's older in-memory copy.
    expect(req.request.body.refreshToken).toBe('stored-refresh');

    req.flush({
      accessToken: 'new-access',
      refreshToken: 'new-refresh',
      accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      refreshTokenExpiresAt: new Date(Date.now() + 7 * 864e5).toISOString(),
    });

    expect((await pending).accessToken).toBe('new-access');

    // The renewed pair is published where the other tabs will look for it.
    expect(JSON.parse(localStorage.getItem(KEY)!).tokens.accessToken).toBe('new-access');
    http.verify();
  });

  /** ★ A token about to expire is not worth adopting: it would 401 on the very next request. */
  it('does not adopt a token that is within the skew of expiring', async () => {
    localStorage.setItem(KEY, session('nearly-dead', 'stored-refresh', 5_000));

    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);

    const pending = firstValueFrom(auth.refresh());
    const req = await awaitRefreshRequest();
    req.flush({
      accessToken: 'new-access',
      refreshToken: 'new-refresh',
      accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      refreshTokenExpiresAt: new Date(Date.now() + 7 * 864e5).toISOString(),
    });

    expect((await pending).accessToken).toBe('new-access');
    http.verify();
  });
});

// ─── The two remaining ACs: proactive renewal and the surviving draft (KAN-1) ──
describe('AuthService — proactive renewal and draft survival', () => {
  const KEY = 'wasnie_session';
  const DRAFT = 'wasnie:draft:assistant:c1';

  function store(expiresInMs: number): void {
    localStorage.setItem(KEY, JSON.stringify({
      tenantId: 't1',
      tokens: {
        accessToken: 'a', refreshToken: 'r',
        accessTokenExpiresAt: new Date(Date.now() + expiresInMs).toISOString(),
        refreshTokenExpiresAt: new Date(Date.now() + 7 * 864e5).toISOString(),
      },
    }));
  }

  beforeEach(() => {
    localStorage.removeItem(KEY);
    sessionStorage.removeItem(DRAFT);
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [{ provide: TabSyncService, useValue: makeTabSyncMock().service }],
    });
  });

  afterEach(() => {
    localStorage.removeItem(KEY);
    sessionStorage.removeItem(DRAFT);
  });

  /** ★ The happy-path AC: a token near the end of its life is renewed BEFORE the request goes out. */
  it('reports a token inside the skew as needing renewal', () => {
    store(5_000);
    expect(TestBed.inject(AuthService).accessTokenNeedsRenewal()).toBeTrue();
  });

  it('leaves a healthy token alone', () => {
    store(10 * 60_000);
    expect(TestBed.inject(AuthService).accessTokenNeedsRenewal()).toBeFalse();
  });

  /**
   * ★★ THE FAIL-SAFE AC. An expiry is not a decision the user made, so the half-written adjustment they
   * were in the middle of has to still be there when they sign back in. This path was DELETING it.
   */
  it('keeps unsent drafts when the session expires involuntarily', () => {
    store(-1000);
    sessionStorage.setItem(DRAFT, 'half a manual adjustment');

    TestBed.inject(AuthService).forceLogout(true);

    expect(sessionStorage.getItem(DRAFT)).toBe('half a manual adjustment');
  });

  /**
   * ★ AND SIGNING OUT STILL CLEARS THEM. The shared-machine argument in `clearFeatureDrafts` is intact:
   * this fix distinguishes the two cases, it does not weaken the deliberate one.
   */
  it('still clears drafts on a deliberate sign-out', () => {
    store(10 * 60_000);
    sessionStorage.setItem(DRAFT, 'half a manual adjustment');

    TestBed.inject(AuthService).logout();

    expect(sessionStorage.getItem(DRAFT)).toBeNull();
  });

  /** ★ The forced sign-out button is a decision too, so it clears as well. */
  it('clears drafts when the user presses sign out on the idle warning', () => {
    store(10 * 60_000);
    sessionStorage.setItem(DRAFT, 'half a manual adjustment');

    TestBed.inject(AuthService).forceLogout(false);

    expect(sessionStorage.getItem(DRAFT)).toBeNull();
  });
});
