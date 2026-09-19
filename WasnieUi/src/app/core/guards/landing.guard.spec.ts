import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { runInInjectionContext, Injector } from '@angular/core';
import { CurrentUserService } from '../auth/current-user.service';
import { companyDashboardGuard, landingRedirect } from './landing.guard';

/**
 * KAN-92 — where a session lands. The decision is the PERMISSION, never the role name, so a role
 * added later needs no edit here.
 */
describe('landing', () => {
  function withPermission(held: boolean) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: CurrentUserService, useValue: { hasPermission: (p: string) => held && p === 'Reports.ViewAll' } },
      ],
    });
    return TestBed.inject(Injector);
  }

  it('sends a reader of company reports to the company dashboard', () => {
    const injector = withPermission(true);
    expect(runInInjectionContext(injector, landingRedirect)).toBe('/dashboard');
  });

  it('sends everybody else to their own dashboard', () => {
    const injector = withPermission(false);
    expect(runInInjectionContext(injector, landingRedirect)).toBe('/my-dashboard');
  });

  it('redirects rather than forbids when somebody opens /dashboard without the permission', () => {
    const injector = withPermission(false);
    const result = runInInjectionContext(injector, () =>
      companyDashboardGuard({} as never, {} as never),
    );

    // ★ A bookmark is not an offence: /forbidden would tell a rep they are not welcome in their own
    // product. They get the screen that answers their question instead.
    expect(result instanceof UrlTree).toBe(true);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/my-dashboard');
  });

  it('lets an administrator through untouched', () => {
    const injector = withPermission(true);
    const result = runInInjectionContext(injector, () =>
      companyDashboardGuard({} as never, {} as never),
    );
    expect(result).toBe(true);
  });
});
