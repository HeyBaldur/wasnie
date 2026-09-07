import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SidebarBadges, SidebarBadgesStore } from './sidebar-badges.store';

describe('SidebarBadgesStore', () => {
  let store: SidebarBadgesStore;
  let http: HttpTestingController;

  const badges = (over: Partial<SidebarBadges> = {}): SidebarBadges => ({
    reconciliation: 3,
    terminatedAccounts: 2,
    financialsTotal: 5,
    ...over,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(SidebarBadgesStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    store.stop();
    http.verify();
  });

  it('asks the single badges endpoint', async () => {
    const loading = store.refresh();
    const req = http.expectOne('/api/sidebar-badges');
    expect(req.request.method).toBe('GET');

    req.flush(badges());
    await loading;

    expect(store.reconciliation()).toBe(3);
    expect(store.terminatedAccounts()).toBe(2);
  });

  /**
   * ★★ null AND 0 ARE DIFFERENT ANSWERS. Null is "you may not see this count" and draws no badge; 0
   * is a real measurement — "this queue is clear" — and is worth showing. Collapsing them would tell
   * a user without the permission that the tenant has no unpaid money, which is a statement about
   * money they were not cleared to receive.
   */
  it('keeps a withheld count apart from a count of zero', async () => {
    const loading = store.refresh();
    http.expectOne('/api/sidebar-badges')
      .flush(badges({ reconciliation: null, terminatedAccounts: 0, financialsTotal: 0 }));
    await loading;

    expect(store.reconciliation()).toBeNull('withheld: no badge at all');
    expect(store.terminatedAccounts()).toBe(0, 'measured: the badge shows 0');
  });

  /** ★ The group row carries no badge when there is nothing — a permanent "0" on a container says nothing. */
  it('hides the group total when it is zero', async () => {
    const loading = store.refresh();
    http.expectOne('/api/sidebar-badges').flush(badges({ financialsTotal: 0 }));
    await loading;

    expect(store.financialsTotal()).toBeNull();
  });

  it('shows the group total when there is work', async () => {
    const loading = store.refresh();
    http.expectOne('/api/sidebar-badges').flush(badges({ financialsTotal: 7 }));
    await loading;

    expect(store.financialsTotal()).toBe(7);
  });

  /**
   * ★★ A FAILED REFRESH KEEPS THE LAST NUMBERS. A badge is decoration on somebody else's screen: a
   * sidebar that empties itself on one bad response looks like work disappearing, which is a worse
   * lie than a number a few minutes old.
   */
  it('keeps the previous counts when the refresh fails', async () => {
    const first = store.refresh();
    http.expectOne('/api/sidebar-badges').flush(badges());
    await first;

    const second = store.refresh();
    http.expectOne('/api/sidebar-badges').flush('boom', { status: 500, statusText: 'Server Error' });
    await second;

    expect(store.reconciliation()).toBe(3, 'the sidebar does not empty itself on a blip');
    expect(store.terminatedAccounts()).toBe(2);
  });

  /**
   * ★ `start()` twice must not stack timers. The sidebar is built once, but a component that is
   * recreated — a re-login, a shell rebuild — would otherwise double the polling for the rest of the
   * session, and again on the next one.
   */
  /**
   * ★★ THE REGRESSION THIS PINS: A REQUEST PER NAVIGATION. The sidebar is rebuilt on every route
   * change — each feature template renders its own app-shell — so `start()` runs on every click. A
   * `start()` that fetched each time would poll the API for the length of the session, which is
   * precisely what this feature was asked not to do. The second call must be silent.
   */
  it('fetches once however many times it is started', async () => {
    store.start();
    http.expectOne('/api/sidebar-badges').flush(badges());

    // ★ `start()` returns void, so the flush needs a turn before its result reaches the signal.
    await new Promise(resolve => setTimeout(resolve, 0));

    store.start();
    store.start();

    // afterEach's verify() fails if either of those queued a request.
    expect(store.reconciliation()).toBe(3);
  });
});
