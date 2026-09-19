import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { PlansStore } from './plans.store';
import { CurrentUserService } from '../../../core/auth/current-user.service';

/**
 * KAN-93 bug 6, follow-up: the plan detail must not be blanked by the plans LIST failing.
 *
 * ★★ THE DEFECT, AND WHY IT LOOKED INTERMITTENT. `loading` and `error` on this store were shared by
 * two unrelated requests — the catalogue and one plan — and the detail template reads `error` BEFORE
 * `selectedPlan`. Opening a plan fires both at once; each clears `error` when it starts and writes it
 * when it fails, so whichever finished LAST decided the screen. Arriving by click the plan usually
 * won; on a refresh the list queued behind a dozen bootstrap calls, landed last, and the page showed
 * "Something went wrong. Please try again." over a plan it had successfully loaded. Reported exactly
 * that way: works on click, fails on refresh.
 *
 * ★★ IT IS NOT A REP-ONLY BUG, WHICH IS WHY THE FIX IS NOT A ROLE CHECK. A rep makes the list fail
 * every time — they hold no `Plans.Read`, by design — so they meet it constantly; for anybody else a
 * slow or failing catalogue does the same thing. One field meaning two things is the defect (§B3).
 */
describe('PlansStore — the detail page has its own state', () => {
  let store: PlansStore;
  let http: HttpTestingController;

  function configure(permissions: string[]): void {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: CurrentUserService,
          useValue: { hasPermission: (p: string) => permissions.includes(p) },
        },
      ],
    });

    store = TestBed.inject(PlansStore);
    http = TestBed.inject(HttpTestingController);
    // Flush the constructor effect, which loads the list for whoever may read it.
    TestBed.tick();
  }

  afterEach(() => http.verify({ ignoreCancelled: true }));

  /**
   * ★★ THE REGRESSION TEST FOR THE EXACT RACE. The list fails AFTER the plan has arrived — the
   * ordering that broke the page on every refresh. Before the fix, `error` was set and the template
   * painted it over a plan that was sitting right there.
   */
  it('keeps the loaded plan when the catalogue fails afterwards', async () => {
    configure(['Plans.Read']);

    const load = store.loadPlan('plan-1');
    http.expectOne(r => r.url.endsWith('/plans/plan-1'))
      .flush({ id: 'plan-1', name: 'Q3', rules: [], status: 'Active' });
    await load;

    // Now the catalogue request loses the race and fails.
    http.expectOne(r => r.url.includes('/plans?') || r.url.endsWith('/plans'))
      .flush('nope', { status: 403, statusText: 'Forbidden' });

    // ★ `flush` is synchronous but the store's catch block is not: it runs in the microtask the
    // awaited request resolves into. Asserting without yielding reads the state one tick too early.
    await new Promise(resolve => setTimeout(resolve));

    expect(store.selectedPlan()).not.toBeNull();
    expect(store.planError()).toBeNull();
    expect(store.planLoading()).toBeFalse();
    // The list's own state may well be in error — that is its business, and the detail ignores it.
    expect(store.error()).toBe('ERRORS.GENERIC');
  });

  /**
   * ★★ AND THE CATALOGUE IS NOT EVEN REQUESTED FOR SOMEBODY WHO MAY NOT READ IT. The effect runs the
   * moment the store is injected, including from the detail page. For a rep that request is a
   * guaranteed 403 and a PermissionDenied audit row every time they open their own plan.
   */
  it('never asks for the catalogue without Plans.Read', async () => {
    configure(['Plans.ReadOwn']);

    const load = store.loadPlan('plan-1');
    http.expectOne(r => r.url.endsWith('/plans/plan-1'))
      .flush({ id: 'plan-1', name: 'Q3', rules: [], status: 'Active' });
    await load;

    http.expectNone(r => r.url.includes('/plans?'));
    expect(store.selectedPlan()).not.toBeNull();
    expect(store.planError()).toBeNull();
  });

  /**
   * ★ A PLAN THAT REALLY WAS REFUSED STILL SAYS SO — and clears the previous one, so the screen never
   * shows an error message above a stale plan.
   */
  it('reports a refused plan on the detail state, and keeps no stale plan', async () => {
    configure(['Plans.ReadOwn']);

    const first = store.loadPlan('plan-1');
    http.expectOne(r => r.url.endsWith('/plans/plan-1'))
      .flush({ id: 'plan-1', name: 'Q3', rules: [], status: 'Active' });
    await first;

    const second = store.loadPlan('plan-2');
    http.expectOne(r => r.url.endsWith('/plans/plan-2'))
      .flush('nope', { status: 403, statusText: 'Forbidden' });
    await second;

    expect(store.planError()).toBe('ERRORS.GENERIC');
    expect(store.selectedPlan()).toBeNull();
  });
});
