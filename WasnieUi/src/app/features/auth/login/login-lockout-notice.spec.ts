import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { LoginComponent } from './login.component';
import { AuthService } from '../../../core/services/auth.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';

/**
 * KAN-21: the login screen must explain a lockout instead of repeating "invalid credentials",
 * and must never print a raw backend code at the user (§C2).
 *
 * ★ THROUGH THE DOM. The point of the change is what the user reads, and a test that asserts on
 * the signal would pass just as happily with the banner wired to a branch the template never
 * renders — which is exactly the failure §A3 keeps catching.
 */
describe('LoginComponent — account lockout notice', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let auth: jasmine.SpyObj<AuthService>;

  function failWith(message: string): void {
    auth.login.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 401, error: { message } })),
    );
  }

  function submit(): void {
    fixture.componentInstance.form.setValue({ email: 'a@example.com', password: 'secret123' });
    fixture.componentInstance.submit();
    fixture.detectChanges();
  }

  function bannerText(): string {
    const el = fixture.nativeElement as HTMLElement;
    return (el.querySelector('.auth-form__error')?.textContent ?? '').trim();
  }

  beforeEach(async () => {
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['login']);
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

    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
  });

  it('renders the lockout notice with the countdown, not the raw code', () => {
    failWith('ACCOUNT_LOCKED:15');

    submit();

    expect(fixture.componentInstance.accountLocked()).toEqual({ minutes: 15 });
    expect(bannerText()).toContain('AUTH.ACCOUNT_LOCKED');
    expect(bannerText()).not.toContain('ACCOUNT_LOCKED:15');
  });

  it('falls back to the countdown-less wording when the minutes are unusable', () => {
    // The account is still locked; printing "ACCOUNT_LOCKED:abc" at the user is the one
    // outcome that is never acceptable.
    failWith('ACCOUNT_LOCKED:not-a-number');

    submit();

    expect(fixture.componentInstance.accountLocked()).toEqual({ minutes: null });
    expect(bannerText()).toContain('AUTH.ACCOUNT_LOCKED_GENERIC');
    expect(bannerText()).not.toContain('not-a-number');
  });

  it('leaves an ordinary failure on the generic message', () => {
    failWith('Invalid credentials.');

    submit();

    expect(fixture.componentInstance.accountLocked()).toBeNull();
    expect(bannerText()).toBe('Invalid credentials.');
  });

  it('does not mistake the unconfirmed-email code for a lockout', () => {
    failWith('EMAIL_NOT_CONFIRMED');

    submit();

    expect(fixture.componentInstance.accountLocked()).toBeNull();
    expect(fixture.componentInstance.emailNotConfirmed()).toBeTrue();
  });

  it('does not treat a lookalike prefix as the lockout code', () => {
    // The code is matched as a literal prefix, never assembled into a translation key.
    failWith('ACCOUNT_LOCKED_SOMETHING_ELSE');

    submit();

    expect(fixture.componentInstance.accountLocked()).toBeNull();
  });

  it('clears a previous lockout banner when the next attempt fails differently', () => {
    failWith('ACCOUNT_LOCKED:15');
    submit();
    expect(fixture.componentInstance.accountLocked()).not.toBeNull();

    failWith('Invalid credentials.');
    submit();

    expect(fixture.componentInstance.accountLocked()).toBeNull();
    expect(bannerText()).toBe('Invalid credentials.');
  });
});
