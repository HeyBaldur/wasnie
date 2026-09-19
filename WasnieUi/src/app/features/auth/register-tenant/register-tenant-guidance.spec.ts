import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { RegisterTenantComponent } from './register-tenant.component';
import { AuthService } from '../../../core/services/auth.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';

/**
 * El alta explicada: el identificador de organización es el campo que decide si el equipo podrá
 * entrar, y es el que nadie entiende al verlo por primera vez.
 *
 * ★ TODO POR EL DOM (§A3). Lo que se construyó es texto en pantalla; comprobar la señal
 * `slugPreview()` pasaría igual con la vista previa sin pintar, que es el modo de fallo real.
 */
describe('RegisterTenantComponent — guía del identificador', () => {
  let fixture: ComponentFixture<RegisterTenantComponent>;

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(selector: string): string {
    return (el().querySelector(selector)?.textContent ?? '').trim();
  }

  beforeEach(async () => {
    const auth = jasmine.createSpyObj<AuthService>('AuthService', ['registerTenant']);
    const currentUser = jasmine.createSpyObj<CurrentUserService>('CurrentUserService', ['refresh']);
    currentUser.refresh.and.returnValue(of(void 0));

    await TestBed.configureTestingModule({
      imports: [RegisterTenantComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: CurrentUserService, useValue: currentUser },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RegisterTenantComponent);
    fixture.detectChanges();
  });

  it('separa el espacio de trabajo de la cuenta que lo administra', () => {
    const titles = Array.from(el().querySelectorAll('.register__section-title'))
      .map(node => node.textContent?.trim());

    expect(titles).toEqual(['AUTH.SECTION_ORGANIZATION', 'AUTH.SECTION_ADMIN']);
  });

  it('explica cada campo bajo el propio campo', () => {
    const hints = Array.from(el().querySelectorAll('.ws-input__hint'))
      .map(node => node.textContent?.trim());

    // ★ EL IDENTIFICADOR LLEVA LA SUYA SÍ O SÍ: es el único campo del formulario que no se puede
    // corregir después y del que depende que el equipo entre.
    expect(hints).toContain('AUTH.TENANT_SLUG_HINT');
    expect(hints).toContain('AUTH.TENANT_NAME_HINT');
  });

  it('enseña lo que el equipo tendrá que escribir, derivado del nombre', () => {
    // Antes de escribir nada, el ejemplo — no un hueco vacío que no explica nada.
    expect(text('.register__preview-value')).toBe('acme-corp');

    fixture.componentInstance.form.controls.tenantName.setValue('Northwind Trading');
    fixture.detectChanges();

    expect(fixture.componentInstance.form.controls.tenantSlug.value).toBe('northwind-trading');
    // ★ LA VISTA PREVIA SIGUE A LA DERIVACIÓN, que es el camino normal. La derivación escribe el
    // control con `emitEvent: false`, así que una vista previa colgada de `valueChanges` se
    // quedaría en el ejemplo mientras el campo ya dice otra cosa.
    expect(text('.register__preview-value')).toBe('northwind-trading');
  });

  it('sigue al identificador cuando se escribe a mano', () => {
    fixture.componentInstance.form.controls.tenantSlug.setValue('acme-polska');
    fixture.detectChanges();

    expect(text('.register__preview-value')).toBe('acme-polska');
  });

  it('el error del campo reemplaza a la explicación, no se apilan', () => {
    const slug = fixture.componentInstance.form.controls.tenantSlug;
    slug.setValue('Acme Corp!');   // mayúsculas y símbolos: no cumple el patrón
    slug.markAsTouched();
    fixture.detectChanges();

    const block = el().querySelector('.register__identifier')!;
    expect(block.querySelector('.ws-input__error')?.textContent?.trim()).toBe('VALIDATION.SLUG_PATTERN');
    expect(block.querySelector('.ws-input__hint')).toBeNull();
  });
});
