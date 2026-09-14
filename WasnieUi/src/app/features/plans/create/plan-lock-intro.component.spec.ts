import { TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';
import { PlanLockIntroComponent } from './plan-lock-intro.component';

describe('PlanLockIntroComponent', () => {
  const render = () => {
    const fixture = TestBed.createComponent(PlanLockIntroComponent);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(() => {
    // Un flag viejo de cuando se podía cerrar no debe esconderla.
    localStorage.setItem('wasnie:plan-lock-intro-dismissed', '1');
    TestBed.configureTestingModule({
      imports: [PlanLockIntroComponent, TranslateModule.forRoot()],
    });
  });

  afterEach(() => localStorage.removeItem('wasnie:plan-lock-intro-dismissed'));

  it('★ siempre está: sin X ni «Entendido», aunque exista el flag viejo de cerrada', () => {
    const el: HTMLElement = render().nativeElement;

    expect(el.querySelector('.lock')).not.toBeNull();
    expect(el.querySelector('.lock__close')).toBeNull();
    expect(el.querySelector('button')).toBeNull();
    expect(el.querySelectorAll('.lock__point').length).toBe(3);
  });
});
