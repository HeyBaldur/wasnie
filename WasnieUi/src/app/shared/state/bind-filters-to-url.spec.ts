import { DestroyRef } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject } from 'rxjs';

import { bindFiltersToUrl } from './bind-filters-to-url';

describe('bindFiltersToUrl', () => {
  let queryParams: BehaviorSubject<Record<string, string>>;
  let route: ActivatedRoute;
  let destroyRef: DestroyRef;
  let applied: Array<Record<string, string>>;
  let resets: number;

  /** Runs the binding inside an injection context so takeUntilDestroyed has a DestroyRef. */
  function bind(initial: Record<string, string>): void {
    queryParams = new BehaviorSubject<Record<string, string>>(initial);
    route = { queryParams: queryParams.asObservable() } as unknown as ActivatedRoute;
    TestBed.runInInjectionContext(() => {
      destroyRef = TestBed.inject(DestroyRef);
      bindFiltersToUrl(route, destroyRef, {
        apply: qp => applied.push(qp),
        reset: () => { resets++; },
      });
    });
  }

  beforeEach(() => {
    TestBed.configureTestingModule({});
    applied = [];
    resets = 0;
  });

  it('applies the filter carried by the URL on entry', () => {
    bind({ status: 'Draft' });

    expect(applied).toEqual([{ status: 'Draft' }]);
    expect(resets).toBe(0);
  });

  // Defect A: Angular reuses the component when only the query params change, so ngOnInit never runs
  // again. A snapshot read would stop here; the subscription must keep going.
  it('re-applies on every later query-param change, not just the first', () => {
    bind({ status: 'Draft' });
    queryParams.next({ status: 'Approved' });
    queryParams.next({ status: 'Paid', period: 'all-time' });

    expect(applied).toEqual([
      { status: 'Draft' },
      { status: 'Approved' },
      { status: 'Paid', period: 'all-time' },
    ]);
  });

  // Defect B: the stores are root singletons, so "no params" must actively restore the default —
  // otherwise the previous visit's filter stays applied under a URL that no longer mentions it.
  it('resets when entering with no params at all', () => {
    bind({});

    expect(resets).toBe(1);
    expect(applied).toEqual([]);
  });

  it('resets when a navigation strips the params from the URL', () => {
    bind({ status: 'Draft' });
    queryParams.next({});

    expect(applied).toEqual([{ status: 'Draft' }]);
    expect(resets).toBe(1);
  });

  it('never both applies and resets for the same emission', () => {
    bind({ status: 'Draft' });
    queryParams.next({});
    queryParams.next({ period: 'ytd' });

    expect(applied.length + resets).toBe(3);
    expect(resets).toBe(1);
  });

  it('stops listening once the injection context is destroyed', () => {
    bind({ status: 'Draft' });
    TestBed.resetTestingModule();          // destroys the injector -> takeUntilDestroyed fires

    queryParams.next({ status: 'Approved' });

    expect(applied).toEqual([{ status: 'Draft' }]);
  });
});
