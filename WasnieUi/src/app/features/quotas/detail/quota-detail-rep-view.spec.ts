import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { QuotaDetailComponent } from './quota-detail.component';
import { QuotasStore } from '../state/quotas.store';
import { CurrentUserService } from '../../../core/auth/current-user.service';

const PAYEE_ID = 'payee-1';

function quota(status: string) {
  return {
    id: 'quota-1',
    payeeId: PAYEE_ID,
    payeeName: 'John Travolta',
    payeeEmployeeCode: 'EMP-TST-001',
    planName: 'PRUEBA',
    status,
    periodStart: '2026-01-01',
    periodEnd: '2026-12-31',
    targetAmount: 1000,
    currency: 'EUR',
    measurement: 'Revenue',
  };
}

/**
 * KAN-93 — the quota a Sales Rep opens about themselves.
 *
 * ★★ TWO DEFECTS OF THE SAME FAMILY, both found in runtime and both invisible to a unit test that
 * only asked the component questions. The page decided what to offer from the QUOTA'S STATUS and
 * never from the reader's authority — the same mistake the plan page made with `getPlanPermissions`.
 * The quota LIST had gated these actions all along, which is precisely what kept the gap hidden.
 */
describe('QuotaDetailComponent — what a Sales Rep may see', () => {
  let fixture: ComponentFixture<QuotaDetailComponent>;

  function mountAs(permissions: string[], status = 'Active'): void {
    TestBed.overrideProvider(CurrentUserService, {
      useValue: { hasPermission: (p: string) => permissions.includes(p) },
    });

    fixture = TestBed.createComponent(QuotaDetailComponent);
    fixture.componentInstance.store.selectedQuota.set(quota(status) as never);
    fixture.detectChanges();
  }

  function actionsText(): string {
    const el = (fixture.nativeElement as HTMLElement).querySelector('.quota-detail__actions');
    return (el?.textContent ?? '').trim();
  }

  function payeeHref(): string | null {
    const el = (fixture.nativeElement as HTMLElement).querySelector('a.field-link');
    return el?.getAttribute('href') ?? null;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuotaDetailComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: QuotasStore,
          useValue: {
            selectedQuota: signal<unknown>(null),
            loading: signal(false),
            error: signal(null),
            loadQuota: jasmine.createSpy('loadQuota').and.returnValue(Promise.resolve()),
            closeQuota: jasmine.createSpy('closeQuota').and.returnValue(Promise.resolve()),
            activateQuota: jasmine.createSpy('activateQuota').and.returnValue(Promise.resolve()),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'quota-1' } } },
        },
        { provide: CurrentUserService, useValue: {} },
      ],
    }).compileComponents();
  });

  /**
   * ★★ THE REPORTED BUG. Closing a quota ends the target somebody's commission is measured against.
   * A rep was offered it on their own quota; the server refused (`Quotas.Set`), so the only way to
   * learn was to press the button and read a toast — the pattern §5.8 exists to end.
   */
  it('offers a rep no way to close their own quota', () => {
    mountAs(['Quotas.Read'], 'Active');

    expect(actionsText()).not.toContain('QUOTAS.ACTION_CLOSE');
  });

  it('offers a rep no way to activate a Draft quota either', () => {
    mountAs(['Quotas.Read'], 'Draft');

    expect(actionsText()).not.toContain('QUOTAS.ACTION_ACTIVATE');
  });

  /**
   * ★★ AND THE ADMINISTRATOR IS UNTOUCHED — asserted next to the negative case, because a gate that
   * also hid the action from whoever runs compensation would be a regression, not a fix. The key is
   * `Quotas.Set`, the one the handlers really ask for: a control whose permission disagrees with its
   * handler is a trap.
   */
  it('still offers Close to somebody holding Quotas.Set', () => {
    mountAs(['Quotas.Read', 'Quotas.Set'], 'Active');

    expect(actionsText()).toContain('QUOTAS.ACTION_CLOSE');
  });

  /**
   * ★★ THE SECOND DEFECT: the payee's name led a rep to Access Denied from a page they were entitled
   * to be on. Payees is hidden from them entirely (bug 1), and their own record is not a payee page.
   */
  it('sends a rep to their own profile instead of a payee page', () => {
    mountAs(['Quotas.Read'], 'Active');

    expect(payeeHref()).toBe('/profile');
  });

  /**
   * ★ THE MANAGER AND THE ADMIN KEEP THE PAYEE PAGE. Keyed on `Payees.Read` — the same permission the
   * /payees route guard asks for — so the link can never offer a page that guard then refuses.
   */
  it('keeps the payee page for anybody who may read payees', () => {
    mountAs(['Quotas.Read', 'Payees.Read'], 'Active');

    expect(payeeHref()).toBe(`/payees/${PAYEE_ID}`);
  });
});
