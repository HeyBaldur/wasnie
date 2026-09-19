import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { PlanDetailComponent } from './plan-detail.component';
import { PlansStore } from '../state/plans.store';
import { CurrentUserService } from '../../../core/auth/current-user.service';

const PLAN = {
  id: 'plan-1',
  name: 'Q3 2026 — EMEA',
  description: 'test',
  status: 'Active',
  version: 1,
  currency: 'EUR',
  effectiveStart: '2026-01-01',
  effectiveEnd: '2026-12-31',
  createdBy: 'admin',
  rules: [],
  activeAssignmentCount: 3,
};

/**
 * KAN-93 bug 6 — the plan page seen by somebody who is PAID under the plan, not somebody who runs it.
 *
 * ★★ THE SEPARATION THE WHOLE TICKET TURNS ON. "Disclosure" is handing a rep their plan; TRANSPARENCY
 * is letting them check the arithmetic — the rate table, the tiers, the cap, the floor — so they stop
 * keeping a private spreadsheet nobody reconciles. That is what a rep is admitted here to read.
 * Administration is a different thing entirely, and two of these tabs are other people's data.
 *
 * ★★ ASSERTED THROUGH THE DOM, because the existing gate could not have done this. `getPlanPermissions`
 * answers a question about the plan's STATUS — "may an Active plan be archived" — and has never known
 * who is asking. A rep admitted to this page without a role gate would have been offered Archive and
 * Clone on any Active plan, and the component would have looked perfectly correct while doing it.
 */
describe('PlanDetailComponent — the read-only view for a rep', () => {
  let fixture: ComponentFixture<PlanDetailComponent>;
  let store: Record<string, jasmine.Spy>;

  function mountAs(permissions: string[]): void {
    TestBed.overrideProvider(CurrentUserService, {
      useValue: {
        currentUser: () => ({ userId: 'u1' }),
        hasPermission: (p: string) => permissions.includes(p),
      },
    });

    fixture = TestBed.createComponent(PlanDetailComponent);
    fixture.detectChanges();
  }

  function tabLabels(): string[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.plan-detail__tabs .tab-btn'),
    ).map(b => (b.textContent ?? '').trim());
  }

  function actionsText(): string {
    const el = (fixture.nativeElement as HTMLElement).querySelector('.plan-detail__actions');
    return (el?.textContent ?? '').trim();
  }

  beforeEach(async () => {
    store = {
      selectedPlan: jasmine.createSpy('selectedPlan').and.returnValue(PLAN),
      loading: jasmine.createSpy('loading').and.returnValue(false),
      error: jasmine.createSpy('error').and.returnValue(null),
      // The detail's own state — the list's is deliberately not what this page reads.
      planLoading: jasmine.createSpy('planLoading').and.returnValue(false),
      planError: jasmine.createSpy('planError').and.returnValue(null),
      versions: jasmine.createSpy('versions').and.returnValue([]),
      loadPlan: jasmine.createSpy('loadPlan').and.returnValue(Promise.resolve()),
      loadVersions: jasmine.createSpy('loadVersions').and.returnValue(Promise.resolve()),
    };

    await TestBed.configureTestingModule({
      imports: [PlanDetailComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PlansStore, useValue: store },
        { provide: CurrentUserService, useValue: {} },
      ],
    }).compileComponents();
  });

  /**
   * ★★ THE POINT OF THE FEATURE. The rules tab is what the rep came for and it is still there; what
   * is gone is everything that configures how the company pays.
   */
  it('shows a rep the rules tab and nothing else', () => {
    mountAs(['Plans.ReadOwn']);

    const tabs = tabLabels();
    expect(tabs.length).toBe(1);
    expect(tabs[0]).toContain('PLANS.TAB_RULES');
    expect(tabs.join(' ')).not.toContain('PLANS.TAB_VERSIONS');
    expect(tabs.join(' ')).not.toContain('PLANS.TAB_ASSIGNMENTS');
    expect(tabs.join(' ')).not.toContain('PLANS.TAB_CLAWBACK');
  });

  /**
   * ★★ THE ASSIGNMENTS TAB IS THE ONE THAT WOULD HAVE LEAKED. It lists who ELSE is on this plan —
   * other payees, by name. Hiding it is not tidiness.
   */
  it('never offers a rep the list of who else is on the plan', () => {
    mountAs(['Plans.ReadOwn']);

    expect((fixture.nativeElement as HTMLElement).textContent)
      .not.toContain('PLANS.TAB_ASSIGNMENTS');
  });

  /**
   * ★★ ARCHIVE AND CLONE ARE OFFERED ON ANY ACTIVE PLAN BY `getPlanPermissions`, which decides on
   * STATUS alone. Without the role gate this assertion fails — which is exactly the defect.
   */
  it('offers a rep no administration actions on an Active plan', () => {
    mountAs(['Plans.ReadOwn']);

    expect(actionsText()).not.toContain('PLANS.ACTION_ARCHIVE');
    expect(actionsText()).not.toContain('PLANS.ACTION_CLONE_VERSION');
    expect(actionsText()).not.toContain('PLANS.ACTION_ADD_RULE');
    expect(actionsText()).not.toContain('PLANS.ACTION_ACTIVATE');
  });

  /**
   * ★★ AND THE VERSION HISTORY IS NOT EVEN REQUESTED. `ListPlanVersions` requires `Plans.Read`;
   * hiding the tab while still firing the call on load would have given the rep a 403 toast on a
   * screen that otherwise worked — the §A3 failure of fixing the view and leaving the request.
   */
  it('does not fetch the version history for a rep', () => {
    mountAs(['Plans.ReadOwn']);

    expect(store['loadVersions']).not.toHaveBeenCalled();
  });

  /**
   * ★★ THE ADMINISTRATOR IS UNTOUCHED. A gate that also hid the tabs from the people who run
   * compensation would not be a fix, it would be a regression — so the positive case is asserted next
   * to the negative one.
   */
  it('leaves an administrator the whole screen', () => {
    mountAs(['Plans.Read']);

    const tabs = tabLabels().join(' ');
    expect(tabs).toContain('PLANS.TAB_RULES');
    expect(tabs).toContain('PLANS.TAB_VERSIONS');
    expect(tabs).toContain('PLANS.TAB_ASSIGNMENTS');
    expect(tabs).toContain('PLANS.TAB_CLAWBACK');
    expect(actionsText()).toContain('PLANS.ACTION_ARCHIVE');
  });
});
