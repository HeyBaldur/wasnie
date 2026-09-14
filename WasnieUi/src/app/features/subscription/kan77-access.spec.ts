import { HttpErrorResponse, HttpRequest, HttpResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, firstValueFrom, of, throwError } from 'rxjs';
import { subscriptionGuard } from '../../core/guards/subscription.guard';
import { planGuard } from '../../core/guards/plan.guard';
import { paymentRequiredInterceptor } from '../../core/interceptors/payment-required.interceptor';
import { AuthService } from '../../core/services/auth.service';
import { CurrentUserService } from '../../core/auth/current-user.service';
import { AccountAccess, SubscriptionService } from './services/subscription.service';
import { SubscriptionStateService } from './services/subscription-state.service';
import { TrialBannerComponent } from './trial-banner/trial-banner.component';

/**
 * KAN-77 — how the client reflects the account's access: the guard and the 402 interceptor that lead a locked
 * account to the paywall, the plan guard that no longer demands a plan before entering, and the trial banner.
 * The server is the authority for all of it; these pin that the client follows, never decides.
 */
const access = (state: AccountAccess['state'], extra: Partial<AccountAccess> = {}): AccountAccess => ({
  state,
  lockReason: state === 'Locked' ? 'TrialEnded' : null,
  trialEndsAt: null,
  trialDaysRemaining: state === 'Trial' ? 5 : null,
  trialLengthDays: state === 'Trial' ? 7 : null,
  assistantTrialMessagesUsed: null,
  assistantTrialMessageLimit: null,
  ...extra,
});

describe('KAN-77 · subscriptionGuard', () => {
  const run = (response: Observable<AccountAccess>) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: SubscriptionService, useValue: { getAccess: () => response } }],
    });
    return TestBed.runInInjectionContext(() =>
      firstValueFrom(subscriptionGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot) as Observable<boolean | UrlTree>));
  };

  it('sends a locked account to the paywall', async () => {
    const result = await run(of(access('Locked')));
    expect(result instanceof UrlTree).toBeTrue();
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/billing/paywall');
  });

  it('lets a trial and a paying account through', async () => {
    expect(await run(of(access('Trial')))).toBeTrue();
    expect(await run(of(access('Active')))).toBeTrue();
  });

  it('★ fails open when the access endpoint is unreachable — the server still enforces the paywall', async () => {
    expect(await run(throwError(() => new HttpErrorResponse({ status: 500 })))).toBeTrue();
  });
});

describe('KAN-77 · planGuard', () => {
  it('★ lets a qualified user in without having selected a plan — the trial needs none', () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isAuthenticated: () => true } },
        { provide: CurrentUserService, useValue: { currentUser: () => ({ emailConfirmed: true, isQualified: true, hasSelectedPlan: false }) } },
      ],
    });

    const result = TestBed.runInInjectionContext(() => planGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));

    expect(result).toBeTrue();
  });
});

describe('KAN-77 · paymentRequiredInterceptor', () => {
  let navigated: string[];

  const intercept = (error: HttpErrorResponse, currentUrl = '/dashboard') => {
    navigated = [];
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [{ provide: Router, useValue: { url: currentUrl, navigateByUrl: (u: string) => navigated.push(u) } }],
    });
    return TestBed.runInInjectionContext(() =>
      firstValueFrom(paymentRequiredInterceptor(new HttpRequest('GET', '/api/payees'), () => throwError(() => error)))
        .catch((e: unknown) => e));
  };

  it('a 402 account_locked leads to the paywall and still propagates the error', async () => {
    const error = new HttpErrorResponse({ status: 402, error: { code: 'account_locked', reason: 'TrialEnded' } });

    const propagated = await intercept(error);

    expect(navigated).toEqual(['/billing/paywall']);
    expect(propagated).toBe(error);
  });

  it('does not react to other errors, nor re-navigate from the paywall itself', async () => {
    await intercept(new HttpErrorResponse({ status: 402, error: { code: 'something_else' } }));
    expect(navigated).toEqual([]);

    await intercept(new HttpErrorResponse({ status: 403 }));
    expect(navigated).toEqual([]);

    await intercept(new HttpErrorResponse({ status: 402, error: { code: 'account_locked' } }), '/billing/paywall');
    expect(navigated).toEqual([]);
  });

  it('passes successful responses untouched', async () => {
    TestBed.configureTestingModule({ providers: [{ provide: Router, useValue: { url: '/', navigateByUrl: () => {} } }] });
    const ok = new HttpResponse({ status: 200 });
    const result = await TestBed.runInInjectionContext(() =>
      firstValueFrom(paymentRequiredInterceptor(new HttpRequest('GET', '/x'), () => of(ok))));
    expect(result).toBe(ok);
  });
});

describe('KAN-77 · TrialBannerComponent', () => {
  const render = (state: AccountAccess['state'] | null, days: number | null, canManage = true) => {
    TestBed.configureTestingModule({
      imports: [TrialBannerComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        { provide: CurrentUserService, useValue: { hasPermission: () => canManage } },
        {
          provide: SubscriptionStateService,
          useValue: { isTrial: signal(state === 'Trial'), trialDaysRemaining: signal(days) },
        },
      ],
    });
    const fixture = TestBed.createComponent(TrialBannerComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('shows the days left while in trial, with the way to subscribe', () => {
    const el = render('Trial', 5);

    expect(el.querySelector('.trial-banner')).not.toBeNull();
    expect(el.textContent).toContain('TRIAL_BANNER.DAYS');
    expect(el.querySelector('.trial-banner--ending')).toBeNull();
    expect(el.querySelector('ws-button')).not.toBeNull();
  });

  it('the last day reads as its own sentence and turns into a warning', () => {
    const el = render('Trial', 1);

    expect(el.textContent).toContain('TRIAL_BANNER.ONE_DAY');
    expect(el.querySelector('.trial-banner--ending')).not.toBeNull();
  });

  it('is absent for a paying account', () => {
    expect(render('Active', null).querySelector('.trial-banner')).toBeNull();
  });

  it('hides the button — not disables it — for someone who cannot subscribe (§5.8)', () => {
    const el = render('Trial', 4, false);

    expect(el.querySelector('.trial-banner')).not.toBeNull();
    expect(el.querySelector('ws-button')).toBeNull();
  });
});
