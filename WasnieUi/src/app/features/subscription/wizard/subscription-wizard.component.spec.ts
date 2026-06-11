import { TestBed, ComponentFixture } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree, provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { signal, Component } from '@angular/core';
import { SubscriptionWizardComponent } from './subscription-wizard.component';
import { SubscriptionService, SubscriptionPlan } from '../services/subscription.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { CurrentUser } from '../../../core/auth/current-user.model';
import { planGuard } from '../../../core/guards/plan.guard';
import { onboardingGuard } from '../../../core/guards/onboarding.guard';
import { AuthService } from '../../../core/services/auth.service';

// ── Shared fixtures ──────────────────────────────────────────────────────────

const SAMPLE_PLANS: SubscriptionPlan[] = [
  { priceId: null,            productId: null,           name: 'Free',    price: 0,   currency: 'EUR', interval: 'free',  tier: 'Free',    maxPayees: 5,   maxPlans: 1,  isCurrentPlan: true  },
  { priceId: 'price_starter', productId: 'prod_starter', name: 'Starter', price: 29,  currency: 'EUR', interval: 'month', tier: 'Starter', maxPayees: 25,  maxPlans: 5,  isCurrentPlan: false },
  { priceId: 'price_growth',  productId: 'prod_growth',  name: 'Growth',  price: 79,  currency: 'EUR', interval: 'month', tier: 'Growth',  maxPayees: 75,  maxPlans: 15, isCurrentPlan: false },
  { priceId: 'price_scale',   productId: 'prod_scale',   name: 'Scale',   price: 199, currency: 'EUR', interval: 'month', tier: 'Scale',   maxPayees: 150, maxPlans: -1, isCurrentPlan: false },
];

function makeUser(hasSelectedPlan: boolean): CurrentUser {
  return { userId: 'u1', email: 'test@test.com', role: 'TenantAdmin', tenantId: 't1',
           tenantSlug: 'slug', tier: 'Free', hasSelectedPlan, permissions: [] };
}

@Component({ template: '', standalone: true })
class StubComponent {}

// ── SubscriptionWizardComponent ───────────────────────────────────────────────

describe('SubscriptionWizardComponent', () => {
  let fixture: ComponentFixture<SubscriptionWizardComponent>;
  let component: SubscriptionWizardComponent;
  let serviceMock: jasmine.SpyObj<SubscriptionService>;
  let router: Router;

  beforeEach(async () => {
    serviceMock = jasmine.createSpyObj<SubscriptionService>('SubscriptionService', ['getPlans', 'selectFree', 'createCheckout']);
    serviceMock.getPlans.and.returnValue(of(SAMPLE_PLANS));

    const cuMock = {
      currentUser: signal<CurrentUser | null>(makeUser(false)),
      refresh: jasmine.createSpy('refresh').and.returnValue(of(undefined)),
    };

    const authMock = {
      isAuthenticated: jasmine.createSpy('isAuthenticated').and.returnValue(true),
      logout: jasmine.createSpy('logout'),
    };

    await TestBed.configureTestingModule({
      imports: [SubscriptionWizardComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'dashboard', component: StubComponent },
          { path: 'auth/login', component: StubComponent },
        ]),
        { provide: SubscriptionService, useValue: serviceMock },
        { provide: CurrentUserService, useValue: cuMock },
        { provide: AuthService, useValue: authMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SubscriptionWizardComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  // ── Rendering ───────────────────────────────────────────────────────────────

  it('renders 4 plan headers after loading', () => {
    const headers = fixture.nativeElement.querySelectorAll('.pt-header');
    expect(headers.length).toBe(4);
  });

  it('shows plan names in the table', () => {
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Free');
    expect(text).toContain('Starter');
    expect(text).toContain('Growth');
    expect(text).toContain('Scale');
  });

  // ── Free plan is actionable ─────────────────────────────────────────────────

  it('only paid plan headers have the --disabled modifier class', () => {
    const disabledHeaders = fixture.nativeElement.querySelectorAll('.pt-header--disabled');
    expect(disabledHeaders.length).toBe(3);
  });

  // ── Paid plans are disabled ─────────────────────────────────────────────────

  it('three paid plan headers have --disabled modifier class', () => {
    const disabledHeaders = fixture.nativeElement.querySelectorAll('.pt-header--disabled');
    expect(disabledHeaders.length).toBe(3);
  });

  // ── Helper methods ─────────────────────────────────────────────────────────

  it('maxPlansDisplay returns "∞" when value is -1', () => {
    const scale = SAMPLE_PLANS.find(p => p.tier === 'Scale')!;
    expect(component.maxPlansDisplay(scale)).toBe('∞');
  });

  it('maxPayeesDisplay returns string count for finite values', () => {
    const starter = SAMPLE_PLANS.find(p => p.tier === 'Starter')!;
    expect(component.maxPayeesDisplay(starter)).toBe('25');
  });

  it('isPaid returns false for Free tier', () => {
    const free = SAMPLE_PLANS.find(p => p.tier === 'Free')!;
    expect(component.isPaid(free)).toBeFalse();
  });

  it('isPaid returns true for paid tiers', () => {
    const growth = SAMPLE_PLANS.find(p => p.tier === 'Growth')!;
    expect(component.isPaid(growth)).toBeTrue();
  });

  // ── selectFree flow ────────────────────────────────────────────────────────

  it('selectFree() calls API, refreshes user, and navigates to /dashboard', async () => {
    serviceMock.selectFree.and.returnValue(of(undefined));
    const navSpy = spyOn(router, 'navigateByUrl').and.returnValue(Promise.resolve(true));
    const cuService = TestBed.inject(CurrentUserService) as unknown as { refresh: jasmine.Spy };

    component.selectFree();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(serviceMock.selectFree).toHaveBeenCalledTimes(1);
    expect(cuService.refresh).toHaveBeenCalledTimes(1);
    expect(navSpy).toHaveBeenCalledWith('/dashboard');
  });

  it('selectFree() clears submitting flag on error', () => {
    serviceMock.selectFree.and.returnValue(throwError(() => new Error('fail')));
    component.selectFree();
    expect(component.submitting()).toBeFalse();
  });

  it('selectFree() is a no-op when already submitting', () => {
    component.submitting.set(true);
    component.selectFree();
    expect(serviceMock.selectFree).not.toHaveBeenCalled();
  });

  // ── selectPaid flow ────────────────────────────────────────────────────────

  it('selectPaid() calls createCheckout with correct priceId and redirects', () => {
    const checkoutUrl = 'https://checkout.stripe.com/test';
    serviceMock.createCheckout.and.returnValue(of({ checkoutUrl }));
    const redirectSpy = spyOn(component as any, 'redirect');

    const paidPlan = SAMPLE_PLANS.find(p => p.tier === 'Starter')!;
    component.selectPaid(paidPlan);

    expect(serviceMock.createCheckout).toHaveBeenCalledWith('price_starter');
    expect(redirectSpy).toHaveBeenCalledWith(checkoutUrl);
  });

  it('selectPaid() sets checkoutError on API failure', () => {
    serviceMock.createCheckout.and.returnValue(throwError(() => new Error('fail')));

    const paidPlan = SAMPLE_PLANS.find(p => p.tier === 'Starter')!;
    component.selectPaid(paidPlan);

    expect(component.checkoutError()).toBeTrue();
    expect(component.submitting()).toBeFalse();
  });

  it('selectPaid() is a no-op when plan has no priceId', () => {
    const freePlan = SAMPLE_PLANS.find(p => p.tier === 'Free')!;
    component.selectPaid(freePlan);
    expect(serviceMock.createCheckout).not.toHaveBeenCalled();
  });

  it('selectPaid() is a no-op when already submitting', () => {
    component.submitting.set(true);
    const paidPlan = SAMPLE_PLANS.find(p => p.tier === 'Starter')!;
    component.selectPaid(paidPlan);
    expect(serviceMock.createCheckout).not.toHaveBeenCalled();
  });

  // ── Error state ────────────────────────────────────────────────────────────

  it('shows error state when getPlans fails', () => {
    serviceMock.getPlans.and.returnValue(throwError(() => new Error('network')));
    component.loadPlans();
    fixture.detectChanges();

    expect(component.loadError()).toBeTrue();
    expect(component.loading()).toBeFalse();
  });

  it('clears error and retries on loadPlans() call', () => {
    serviceMock.getPlans.and.returnValue(of(SAMPLE_PLANS));
    component.loadPlans();
    expect(component.loadError()).toBeFalse();
    expect(component.plans().length).toBe(4);
  });
});

// ── planGuard ────────────────────────────────────────────────────────────────

describe('planGuard', () => {
  function runGuard(isAuthenticated: boolean, hasSelectedPlan: boolean): boolean | UrlTree {
    const authMock = { isAuthenticated: jasmine.createSpy().and.returnValue(isAuthenticated) };
    const cuMock = {
      currentUser: signal<CurrentUser | null>(
        isAuthenticated ? makeUser(hasSelectedPlan) : null
      ),
    };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authMock },
        { provide: CurrentUserService, useValue: cuMock },
      ],
    });

    return TestBed.runInInjectionContext(() =>
      planGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
    ) as boolean | UrlTree;
  }

  it('allows navigation when authenticated and plan selected', () => {
    expect(runGuard(true, true)).toBeTrue();
  });

  it('redirects to /onboarding/plan when authenticated but no plan selected', () => {
    const result = runGuard(true, false);
    expect(result instanceof UrlTree).toBeTrue();
    expect((result as UrlTree).toString()).toContain('onboarding/plan');
  });

  it('redirects to /auth/login when not authenticated', () => {
    const result = runGuard(false, false);
    expect(result instanceof UrlTree).toBeTrue();
    expect((result as UrlTree).toString()).toContain('auth/login');
  });
});

// ── onboardingGuard ──────────────────────────────────────────────────────────

describe('onboardingGuard', () => {
  function runGuard(isAuthenticated: boolean, hasSelectedPlan: boolean): boolean | UrlTree {
    const authMock = { isAuthenticated: jasmine.createSpy().and.returnValue(isAuthenticated) };
    const cuMock = {
      currentUser: signal<CurrentUser | null>(
        isAuthenticated ? makeUser(hasSelectedPlan) : null
      ),
    };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authMock },
        { provide: CurrentUserService, useValue: cuMock },
      ],
    });

    return TestBed.runInInjectionContext(() =>
      onboardingGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
    ) as boolean | UrlTree;
  }

  it('allows navigation when authenticated and plan NOT yet selected', () => {
    expect(runGuard(true, false)).toBeTrue();
  });

  it('redirects to /dashboard when authenticated and plan already selected', () => {
    const result = runGuard(true, true);
    expect(result instanceof UrlTree).toBeTrue();
    expect((result as UrlTree).toString()).toContain('dashboard');
  });

  it('redirects to /auth/login when not authenticated', () => {
    const result = runGuard(false, false);
    expect(result instanceof UrlTree).toBeTrue();
    expect((result as UrlTree).toString()).toContain('auth/login');
  });
});
