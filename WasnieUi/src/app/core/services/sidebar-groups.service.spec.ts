import { TestBed } from '@angular/core/testing';
import { SidebarGroupsService } from './sidebar-groups.service';

/**
 * The blink this service exists to remove.
 *
 * ★★ THE SIDEBAR IS DESTROYED AND REBUILT ON EVERY NAVIGATION — each of the 41 feature templates
 * renders its own `<app-shell>`, and the shell holds the sidebar. While the open-groups set was a
 * signal on the component, each rebuild began with every group CLOSED and the auto-expand effect
 * reopened the active one a frame later: the group collapsed and sprang back on every click, and a
 * group the user had opened by hand was forgotten outright.
 *
 * A root-provided service is not a detail here — it IS the fix, so it is worth a test that says so.
 */
describe('SidebarGroupsService', () => {
  let service: SidebarGroupsService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(SidebarGroupsService);
  });

  it('starts with every group closed', () => {
    expect(service.isExpanded('pay-financials')).toBe(false);
  });

  it('toggles a group open and shut', () => {
    service.toggle('pay-financials');
    expect(service.isExpanded('pay-financials')).toBe(true);

    service.toggle('pay-financials');
    expect(service.isExpanded('pay-financials')).toBe(false);
  });

  /** ★ The auto-expand path: opening an already-open group must not close it. */
  it('open() is idempotent, unlike toggle()', () => {
    service.open('pay-financials');
    service.open('pay-financials');

    expect(service.isExpanded('pay-financials')).toBe(true);
  });

  /**
   * ★★ THE ACTUAL CLAIM: the state outlives whatever component read it. Injecting again is what a
   * rebuilt sidebar does, and the set has to still be there — otherwise the group collapses and the
   * blink is back.
   */
  it('keeps the open groups when the sidebar is rebuilt', () => {
    service.toggle('pay-financials');

    const afterRebuild = TestBed.inject(SidebarGroupsService);

    expect(afterRebuild.isExpanded('pay-financials'))
      .withContext('a rebuilt sidebar must not start from an empty set')
      .toBe(true);
  });
});
