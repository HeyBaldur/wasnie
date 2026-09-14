import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AssignmentPrereqIntroComponent } from './assignment-prereq-intro.component';
import { CurrentUserService } from '../../../core/auth/current-user.service';

describe('AssignmentPrereqIntroComponent', () => {
  const render = (canCreate: boolean) => {
    TestBed.configureTestingModule({
      imports: [AssignmentPrereqIntroComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        { provide: CurrentUserService, useValue: { hasPermission: (p: string) => canCreate && p === 'Assignments.Create' } },
      ],
    });
    const fixture = TestBed.createComponent(AssignmentPrereqIntroComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('★ dice el orden: payee → plan → vincularlos, con enlace a payees y planes', () => {
    const el = render(true);

    expect(el.querySelectorAll('.link__step').length).toBe(3);
    const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('a.link__step-title--link'))
      .map(a => a.getAttribute('href'));
    expect(links).toEqual(['/payees', '/plans']);
    expect(el.querySelector('.link__lead')?.textContent).toContain('ASSIGNMENTS.PREREQ_INTRO.LEAD');
  });

  it('el botón de crear se oculta sin Assignments.Create (oculto, no deshabilitado)', () => {
    expect(render(false).querySelector<HTMLElement>('.link__actions')?.hidden).toBeTrue();
  });

  it('con Assignments.Create el botón de crear está visible', () => {
    expect(render(true).querySelector<HTMLElement>('.link__actions')?.hidden).toBeFalse();
  });
});
