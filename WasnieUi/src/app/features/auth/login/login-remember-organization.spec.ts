import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { LoginComponent } from './login.component';
import { AuthService } from '../../../core/services/auth.service';
import { AuthResult } from '../../../core/models/auth.model';
import { CurrentUserService } from '../../../core/auth/current-user.service';

const KEY = 'wasnie:last-organization';

/**
 * KAN-93, Bug 4 — the half that stops the identifier being forgotten in the first place.
 *
 * ★★ THE RECOVERY EMAIL IS THE CURE AND THIS IS THE PREVENTION. People forget the Organization
 * identifier because they type it once at setup and never see it again; remembering the one that
 * worked removes the occasion entirely for everybody on their own machine.
 *
 * ★★ REMEMBERED ONLY AFTER THE SERVER ACCEPTED IT, which is the assertion that actually matters here.
 * Storing what was typed would remember typos, and a wrong remembered identifier is worse than none:
 * every later sign-in fails while the field looks innocently filled in.
 *
 * ★ THE LINK TO THE RECOVERY PAGE IS ASSERTED THROUGH THE DOM (§A3). A route that exists and a
 * template that never renders the anchor is the failure mode this repo keeps meeting; checking the
 * component would not have caught it.
 */
describe('LoginComponent — remembering the Organization identifier', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let auth: jasmine.SpyObj<AuthService>;

  async function mount(): Promise<void> {
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
  }

  function signInWith(organizationId: string): void {
    fixture.componentInstance.form.setValue({
      email: 'a@example.com',
      password: 'secret123',
      organizationId,
    });
    fixture.componentInstance.submit();
    fixture.detectChanges();
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

  it('prefills the field with the identifier this browser last used', async () => {
    localStorage.setItem(KEY, 'acme-corp');

    await mount();

    expect(fixture.componentInstance.form.controls.organizationId.value).toBe('acme-corp');
  });

  it('starts empty when this browser has never signed in', async () => {
    await mount();

    expect(fixture.componentInstance.form.controls.organizationId.value).toBe('');
  });

  it('remembers the identifier only after the server accepted it', async () => {
    await mount();

    expect(localStorage.getItem(KEY)).toBeNull();

    signInWith('acme-polska');

    expect(localStorage.getItem(KEY)).toBe('acme-polska');
  });

  it('forgets the identifier when the field is submitted empty', async () => {
    localStorage.setItem(KEY, 'acme-corp');

    await mount();
    signInWith('');

    // ★ Emptying the field is somebody saying "stop filling this in". Leaving the old value would
    // make it reappear on the next visit, which reads as the form undoing them.
    expect(localStorage.getItem(KEY)).toBeNull();
  });

  it('offers a way out to somebody who cannot remember it', async () => {
    await mount();

    const link = (fixture.nativeElement as HTMLElement)
      .querySelector('a[href="/auth/forgot-organization"]');

    expect(link).not.toBeNull();
  });
});
