import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { LoginComponent } from './login.component';
import { AuthService } from '../../../core/services/auth.service';
import { AuthResult } from '../../../core/models/auth.model';
import { CurrentUserService } from '../../../core/auth/current-user.service';

const KEY = 'wasnie:last-organization';

/**
 * Las dos pestañas de la pantalla de acceso: administrador (correo + contraseña) y miembro del
 * equipo (correo + contraseña + identificador de organización).
 *
 * ★ TODO SE MIDE EN EL DOM (§A3). Lo que se construyó es lo que la persona VE: un campo que está o
 * no está. Un test contra la señal `userType()` pasaría igual con el campo pintado siempre, que es
 * exactamente el defecto que este repo se encuentra una y otra vez.
 */
describe('LoginComponent — pestañas de tipo de usuario', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let auth: jasmine.SpyObj<AuthService>;

  function mount(): void {
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function organizationField(): HTMLInputElement | null {
    return el().querySelector('input[placeholder="acme-corp"]');
  }

  function tabs(): HTMLButtonElement[] {
    return Array.from(el().querySelectorAll('.ws-tabs__tab'));
  }

  function clickTab(index: number): void {
    tabs()[index].click();
    fixture.detectChanges();
  }

  function fill(email: string, password: string): void {
    fixture.componentInstance.form.patchValue({ email, password });
  }

  beforeEach(async () => {
    localStorage.removeItem(KEY);

    auth = jasmine.createSpyObj<AuthService>('AuthService', ['login']);
    auth.login.and.returnValue(of({ requiresTwoFactor: false } as unknown as AuthResult));

    const currentUser = jasmine.createSpyObj<CurrentUserService>('CurrentUserService', ['refresh']);
    currentUser.refresh.and.returnValue(of(void 0));

    await TestBed.configureTestingModule({
      imports: [LoginComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: CurrentUserService, useValue: currentUser },
      ],
    }).compileComponents();
  });

  afterEach(() => localStorage.removeItem(KEY));

  it('abre en la pestaña de administrador, con correo y contraseña y nada más', () => {
    mount();

    expect(tabs().length).toBe(2);
    expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
    expect(organizationField()).toBeNull();
  });

  it('la pestaña elegida lleva la marca de confirmación', () => {
    mount();

    expect(tabs()[0].querySelector('.ws-tabs__check')).not.toBeNull();
    expect(tabs()[1].querySelector('.ws-tabs__check')).toBeNull();

    clickTab(1);

    expect(tabs()[0].querySelector('.ws-tabs__check')).toBeNull();
    expect(tabs()[1].querySelector('.ws-tabs__check')).not.toBeNull();
  });

  it('la pestaña de miembro muestra el identificador de organización', () => {
    mount();

    clickTab(1);

    expect(organizationField()).not.toBeNull();
  });

  it('en la pestaña de miembro el identificador es obligatorio', () => {
    mount();
    clickTab(1);
    fill('a@example.com', 'secret123');

    fixture.componentInstance.submit();

    // ★ NO SE LLAMA AL SERVIDOR. Quien elige "miembro del equipo" dice que su identificador existe;
    // enviarlo vacío sólo produciría un fallo de credenciales que no explica nada.
    expect(auth.login).not.toHaveBeenCalled();
  });

  it('volver a administrador vacía el identificador y deja entrar sin él', () => {
    mount();
    clickTab(1);
    fixture.componentInstance.form.patchValue({ organizationId: 'acme-corp' });

    clickTab(0);
    fill('a@example.com', 'secret123');
    fixture.componentInstance.submit();

    // ★ EL VALOR NO VIAJA ESCONDIDO. Enviar un identificador que ya no está en pantalla haría
    // fallar el acceso por algo que la persona no puede ver ni corregir.
    expect(auth.login).toHaveBeenCalledWith(
      jasmine.objectContaining({ organizationId: '' }),
    );
  });

  it('un identificador recordado abre la pantalla en la pestaña de miembro', () => {
    localStorage.setItem(KEY, 'acme-corp');

    mount();

    expect(tabs()[1].getAttribute('aria-selected')).toBe('true');
    expect(organizationField()?.value).toBe('acme-corp');
  });

  it('si la API pide el identificador, la pestaña cambia sola y el campo aparece', () => {
    auth.login.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 401, error: { message: 'ORGANIZATION_REQUIRED' } })),
    );
    mount();
    fill('a@example.com', 'secret123');

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(tabs()[1].getAttribute('aria-selected')).toBe('true');
    expect(organizationField()).not.toBeNull();
  });
});
