import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { MyDashboardComponent } from './my-dashboard.component';
import { MyDashboardStore } from './state/my-dashboard.store';
import { MyDashboard } from './models/my-dashboard.model';

function dashboard(over: Partial<MyDashboard> = {}): MyDashboard {
  return {
    linked: true,
    from: '2026-09-01',
    to: '2026-09-30',
    payeeId: 'payee-1',
    payeeName: 'Ana García',
    summary: null,
    quotas: [],
    salesAwaitingSetup: 0,
    ...over,
  };
}

/**
 * KAN-94 — the notice that makes a stuck sale visible to the person who made it.
 *
 * ★★ ASSERTED THROUGH THE DOM, because the whole defect was that nothing reached the DOM. A test on
 * `store.salesAwaitingSetup()` would pass with the template never rendering the block — which is the
 * state the product was already in, since the number simply did not exist.
 *
 * ★★ AND THE NEGATIVE CASES MATTER AS MUCH AS THE POSITIVE ONE. A warning that is always on screen is
 * a warning nobody reads on the day it means something, so "zero shows nothing" and "a failed request
 * shows nothing" are the two that keep it meaningful.
 */
describe('MyDashboardComponent — sales awaiting setup', () => {
  let fixture: ComponentFixture<MyDashboardComponent>;
  let store: {
    error: jasmine.Spy; loading: jasmine.Spy; linked: jasmine.Spy;
    balances: jasmine.Spy; quotas: jasmine.Spy; payeeName: jasmine.Spy;
    payeeId: jasmine.Spy; hasMoney: jasmine.Spy; salesAwaitingSetup: jasmine.Spy;
    dashboard: jasmine.Spy; refresh: jasmine.Spy; load: jasmine.Spy;
    // KAN-98 - the component seeds its range picker from `range()` and labels the figures from
    // `appliedRange()`. A double without them throws before a single assertion in this file runs.
    range: jasmine.Spy; appliedRange: jasmine.Spy; setRange: jasmine.Spy;
  };

  function mountWith(data: MyDashboard | null, error: string | null = null): void {
    store.error.and.returnValue(error);
    store.linked.and.returnValue(data?.linked ?? null);
    store.balances.and.returnValue(data?.summary?.byCurrency ?? []);
    store.quotas.and.returnValue(data?.quotas ?? []);
    store.payeeName.and.returnValue(data?.payeeName ?? null);
    store.payeeId.and.returnValue(data?.payeeId ?? null);
    store.hasMoney.and.returnValue(false);
    store.dashboard.and.returnValue(data);
    store.appliedRange.and.returnValue(
      data?.from && data?.to ? { from: data.from, to: data.to } : null);
    // The store clamps an unknown answer to 0 — mirrored here so the spec exercises the same rule.
    store.salesAwaitingSetup.and.returnValue(data?.salesAwaitingSetup ?? 0);

    fixture = TestBed.createComponent(MyDashboardComponent);
    fixture.detectChanges();
  }

  function noticeText(): string {
    const el = (fixture.nativeElement as HTMLElement).querySelector('.my-dash__pending');
    return (el?.textContent ?? '').trim();
  }

  beforeEach(async () => {
    store = {
      error: jasmine.createSpy('error').and.returnValue(null),
      loading: jasmine.createSpy('loading').and.returnValue(false),
      linked: jasmine.createSpy('linked').and.returnValue(true),
      balances: jasmine.createSpy('balances').and.returnValue([]),
      quotas: jasmine.createSpy('quotas').and.returnValue([]),
      payeeName: jasmine.createSpy('payeeName').and.returnValue('Ana García'),
      payeeId: jasmine.createSpy('payeeId').and.returnValue('payee-1'),
      hasMoney: jasmine.createSpy('hasMoney').and.returnValue(false),
      salesAwaitingSetup: jasmine.createSpy('salesAwaitingSetup').and.returnValue(0),
      dashboard: jasmine.createSpy('dashboard').and.returnValue(null),
      refresh: jasmine.createSpy('refresh'),
      load: jasmine.createSpy('load').and.returnValue(Promise.resolve()),
      range: jasmine.createSpy('range')
        .and.returnValue({ from: '2026-09-01', to: '2026-09-30' }),
      appliedRange: jasmine.createSpy('appliedRange')
        .and.returnValue({ from: '2026-09-01', to: '2026-09-30' }),
      setRange: jasmine.createSpy('setRange'),
    };

    await TestBed.configureTestingModule({
      imports: [MyDashboardComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MyDashboardStore, useValue: store },
      ],
    }).compileComponents();
  });

  it('tells the payee when a sale of theirs cannot be paid on yet', () => {
    mountWith(dashboard({ salesAwaitingSetup: 1 }));

    expect(noticeText()).toContain('MY_DASHBOARD.AWAITING_SETUP_TITLE');
    expect(noticeText()).toContain('MY_DASHBOARD.AWAITING_SETUP_DESC');
  });

  /**
   * ★★ NO AMOUNT, EVER. The sale is €5,000 and the commission on it may be nothing — what they are
   * owed is unknowable until an administrator finishes the setup. A figure here would be read as a
   * promise (§C3), so the component must never be handed one to render.
   */
  it('shows no money figure in the notice', () => {
    mountWith(dashboard({ salesAwaitingSetup: 1 }));

    expect(noticeText()).not.toMatch(/[€$£]|\d{3,}/);
  });

  it('says nothing when every sale of theirs has been processed', () => {
    mountWith(dashboard({ salesAwaitingSetup: 0 }));

    expect((fixture.nativeElement as HTMLElement).querySelector('.my-dash__pending')).toBeNull();
  });

  /**
   * ★ AN UNANSWERED REQUEST IS NOT AN ALARM. Warning somebody that their pay is stuck on the strength
   * of a request that never arrived would be inventing the problem.
   */
  it('says nothing when the request failed', () => {
    mountWith(null, 'boom');

    expect((fixture.nativeElement as HTMLElement).querySelector('.my-dash__pending')).toBeNull();
  });

  /**
   * ★★ THE UNLINKED PERSON GETS THE OTHER MESSAGE, NOT THIS ONE. They have no payee record at all, so
   * "your sales are stuck" would be about sales that cannot exist; what they need to read is that
   * nobody has attached them yet. The two states must never both be on screen.
   */
  it('leaves the unlinked message alone', () => {
    mountWith(dashboard({ linked: false, salesAwaitingSetup: 0 }));

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.my-dash__pending')).toBeNull();
    expect(el.textContent).toContain('MY_DASHBOARD.UNLINKED_TITLE');
  });
});
