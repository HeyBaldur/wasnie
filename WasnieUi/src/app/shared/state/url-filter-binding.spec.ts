/**
 * Cross-screen contract for the URL→filter binding (WI-2).
 *
 * Every list screen was migrated from `route.snapshot.queryParams` in `ngOnInit` to
 * `bindFiltersToUrl`. These tests pin the two behaviours the migration exists for, screen by screen,
 * at the level the screens actually share: the handler pair. A snapshot read passes the first
 * assertion of each pair and fails the second, which is exactly the bug that shipped for months.
 */
import { DestroyRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { BehaviorSubject } from 'rxjs';

import { bindFiltersToUrl, UrlFilterHandlers } from './bind-filters-to-url';

/** Minimal stand-in for a screen: records what the URL asked it to do. */
class ScreenDouble {
  applied: Array<Record<string, string>> = [];
  resets = 0;

  readonly handlers: UrlFilterHandlers = {
    apply: qp => this.applied.push(qp),
    reset: () => { this.resets++; },
  };
}

describe('URL→filter binding — contract shared by every migrated screen', () => {
  let params: BehaviorSubject<Record<string, string>>;
  let screen: ScreenDouble;

  function enterWith(qp: Record<string, string>): void {
    params = new BehaviorSubject<Record<string, string>>(qp);
    screen = new ScreenDouble();
    const route = { queryParams: params.asObservable() } as unknown as ActivatedRoute;
    TestBed.runInInjectionContext(() => {
      bindFiltersToUrl(route, TestBed.inject(DestroyRef), screen.handlers);
    });
  }

  beforeEach(() => TestBed.configureTestingModule({}));

  // One row per migrated screen, using that screen's real deep-link params.
  const SCREENS: Array<{ name: string; deepLink: Record<string, string>; then: Record<string, string> }> = [
    { name: 'Credits',       deepLink: { payeeIds: 'p1' },     then: { payeeIds: 'p2' } },
    { name: 'Assignments',   deepLink: { payeeId: 'p1' },      then: { payeeId: 'p1', status: 'Active' } },
    { name: 'Payees',        deepLink: { status: 'Active' },   then: { status: 'Terminated' } },
    { name: 'Plans',         deepLink: { status: 'Draft' },    then: { status: 'Archived' } },
    { name: 'Quotas',        deepLink: { status: 'Draft' },    then: { status: 'Closed' } },
    { name: 'plan-detail',   deepLink: { tab: 'assignments' }, then: { tab: 'versions' } },
    { name: 'payee-detail',  deepLink: { tab: 'ledger' },      then: { period: 'ytd' } },
  ];

  for (const s of SCREENS) {
    describe(s.name, () => {
      it('applies the deep-link params on entry', () => {
        enterWith(s.deepLink);

        expect(screen.applied).toEqual([s.deepLink]);
        expect(screen.resets).toBe(0);
      });

      // Defect A. Angular reuses the component when only the query params change, so ngOnInit does
      // not run again — a snapshot read would stop after the first value.
      it('re-applies when the URL changes while the component stays mounted', () => {
        enterWith(s.deepLink);
        params.next(s.then);

        expect(screen.applied).toEqual([s.deepLink, s.then]);
      });

      // Defect B. The stores are root singletons and outlive the component, so arriving with a bare
      // URL has to actively restore the screen's default — silence leaves the last visit's filter on.
      it('resets to the screen default when entered with no params', () => {
        enterWith({});

        expect(screen.resets).toBe(1);
        expect(screen.applied).toEqual([]);
      });

      it('resets when a later navigation strips the params away', () => {
        enterWith(s.deepLink);
        params.next({});

        expect(screen.resets).toBe(1);
      });
    });
  }
});
