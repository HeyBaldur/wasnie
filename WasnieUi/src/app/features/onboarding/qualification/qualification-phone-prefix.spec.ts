import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { QualificationComponent } from './qualification.component';
import { OnboardingService } from '../services/onboarding.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';

/**
 * El prefijo del país se dibuja dentro del campo y el usuario sólo teclea los dígitos locales.
 *
 * ★★ LO QUE ESTA PRUEBA PROTEGE ES EL CONTRATO, NO EL ADORNO. Al sacar el prefijo del valor del
 * control, el número internacional pasó a componerse en el momento de enviar. Si alguien deshace esa
 * composición, la pantalla sigue viéndose idéntica y la API empieza a recibir «612345678» sin país —
 * un teléfono que no identifica a nadie, y nada en la interfaz lo delataría.
 *
 * ★ Y SE MIRA LA SALIDA, NO EL CAMINO (§A3): se comprueba el DOM que ve el usuario y el objeto que
 * sale hacia la API, no las señales intermedias que deberían producirlos.
 */
describe('QualificationComponent — el prefijo del país', () => {
  let fixture: ComponentFixture<QualificationComponent>;
  let onboarding: jasmine.SpyObj<OnboardingService>;

  function fill(country: string, digits: string): void {
    const form = fixture.componentInstance.form;
    form.get('country')!.setValue(country);
    form.get('phoneNumber')!.setValue(digits);
    form.get('howHeardAboutUs')!.setValue('referral');
    form.get('salesVolumeRange')!.setValue('1m_5m');
    form.get('currentSystem')!.setValue('excel');
    form.get('legalAccepted')!.setValue(true);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    onboarding = jasmine.createSpyObj<OnboardingService>('OnboardingService', ['qualify']);
    onboarding.qualify.and.returnValue(of(void 0));
    const currentUser = jasmine.createSpyObj<CurrentUserService>('CurrentUserService', ['refresh']);
    currentUser.refresh.and.returnValue(of(void 0));

    await TestBed.configureTestingModule({
      imports: [QualificationComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: OnboardingService, useValue: onboarding },
        { provide: CurrentUserService, useValue: currentUser },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(QualificationComponent);
    fixture.detectChanges();
  });

  it('muestra el prefijo del país dentro del campo al elegir el país', () => {
    fill('ES', '612345678');

    const prefix = (fixture.nativeElement as HTMLElement).querySelector('.ws-input__prefix-text');
    expect(prefix?.textContent?.trim()).toBe('+34');
  });

  it('envía el número internacional completo aunque el campo sólo tenga los dígitos', () => {
    fill('ES', '612345678');

    expect(fixture.componentInstance.form.get('phoneNumber')!.value).toBe('612345678');

    fixture.componentInstance.submit();

    expect(onboarding.qualify).toHaveBeenCalledTimes(1);
    expect(onboarding.qualify.calls.mostRecent().args[0].phoneNumber).toBe('+34 612345678');
  });

  /** Escribir el prefijo a mano no lo duplica: el campo se queda con los dígitos y nada más. */
  it('descarta lo que no sean dígitos si el usuario los teclea', () => {
    fill('ES', '+34 612 345 678');

    expect(fixture.componentInstance.form.get('phoneNumber')!.value).toBe('346123456');
  });
});
