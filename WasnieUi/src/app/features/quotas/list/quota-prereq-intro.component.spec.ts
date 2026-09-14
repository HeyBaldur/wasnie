import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { QuotaPrereqIntroComponent } from './quota-prereq-intro.component';
import { CurrentUserService } from '../../../core/auth/current-user.service';

describe('QuotaPrereqIntroComponent', () => {
  const render = (canSet: boolean) => {
    TestBed.configureTestingModule({
      imports: [QuotaPrereqIntroComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        { provide: CurrentUserService, useValue: { hasPermission: (p: string) => canSet && p === 'Quotas.Set' } },
      ],
    });
    const fixture = TestBed.createComponent(QuotaPrereqIntroComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('★ dice el orden: payee → plan → cuota, con enlace a payees y planes', () => {
    const el = render(true);

    const steps = el.querySelectorAll('.prereq__step');
    expect(steps.length).toBe(3);

    const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('a.prereq__step-title--link'))
      .map(a => a.getAttribute('href'));
    expect(links).toEqual(['/payees', '/plans']);
    expect(steps[1].textContent).toContain('QUOTAS.PREREQ_INTRO.STEP_PLAN_DESC');
  });

  it('el botón de crear se oculta sin Quotas.Set (oculto, no deshabilitado)', () => {
    const actions = render(false).querySelector<HTMLElement>('.prereq__actions');
    expect(actions?.hidden).toBeTrue();
  });

  it('con Quotas.Set el botón de crear está visible', () => {
    const actions = render(true).querySelector<HTMLElement>('.prereq__actions');
    expect(actions?.hidden).toBeFalse();
  });
});
