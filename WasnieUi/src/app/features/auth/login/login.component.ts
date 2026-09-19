import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { ThemeToggleComponent } from '../../../shared/components/theme-toggle/theme-toggle.component';
import { LanguageToggleComponent } from '../../../shared/components/language-toggle/language-toggle.component';
import { WsInputComponent, WsButtonComponent, WsTabsComponent, type WsTab } from '../../../shared/ui';
import { ToastService } from '../../../shared/services/toast.service';
import { SESSION_EXPIRED_NOTICE_KEY } from '../../../core/services/session-exit.service';

/** Quién dice ser quien entra: el dueño del espacio de trabajo, o alguien invitado a uno. */
export type LoginUserType = 'admin' | 'member';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, TranslatePipe, RouterLink, ThemeToggleComponent,
    LanguageToggleComponent, WsInputComponent, WsButtonComponent, WsTabsComponent],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly translate = inject(TranslateService);

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly emailNotConfirmed = signal(false);
  /** null = not locked. minutes === null = locked, but the API did not give a usable countdown. */
  readonly accountLocked = signal<{ minutes: number | null } | null>(null);

  /** KAN-91. True once the API has said the address is in more than one workspace. */
  readonly organizationRequired = signal(false);
  readonly alreadyConfirmed = signal(
    this.route.snapshot.queryParamMap.has('alreadyConfirmed')
  );
  readonly passwordResetSuccess = signal(
    this.route.snapshot.queryParamMap.has('passwordReset')
  );

  /**
   * KAN-93. Where this browser remembers the last Organization identifier that worked.
   *
   * ★ A NAMESPACED localStorage KEY, LIKE THE REST OF THIS APP'S. Not a cookie: it is never sent
   * anywhere, it is read by exactly one screen, and a cookie would ride on every request for nothing.
   */
  private static readonly LAST_ORGANIZATION_KEY = 'wasnie:last-organization';

  /**
   * Quién está entrando: el administrador del espacio de trabajo o alguien invitado a uno.
   *
   * ★ DOS PESTAÑAS, NO UN CAMPO QUE APARECE Y DESAPARECE. El identificador de organización sólo
   * significa algo para quien fue invitado; al administrador le sobra. Separarlo en pestañas dice en
   * una línea a quién le toca cada formulario, en vez de dejar un campo extra que la mayoría no sabe
   * si tiene que rellenar.
   *
   * ★ ARRANCA EN «ADMINISTRADOR» SALVO QUE ESTE NAVEGADOR RECUERDE UN IDENTIFICADOR. Si lo recuerda,
   * quien entró la última vez era un miembro del equipo, y abrir en su pestaña le ahorra el clic —
   * además de que si no, el valor recordado quedaría escondido en una pestaña que no ve.
   */
  readonly userType = signal<LoginUserType>(
    LoginComponent.rememberedOrganization() ? 'member' : 'admin'
  );

  readonly userTypeTabs: WsTab[] = [
    { value: 'admin', label: 'AUTH.USER_TYPE_ADMIN' },
    { value: 'member', label: 'AUTH.USER_TYPE_MEMBER' },
  ];

  /**
   * Cambiar de pestaña cambia el formulario, no sólo lo que se ve.
   *
   * ★ LA PESTAÑA DE ADMINISTRADOR VACÍA EL CAMPO. Dejar un identificador escrito en un campo que ya
   * no está en pantalla lo enviaría a ciegas: quien eligió «administrador» vería fallar el acceso
   * por un valor que no puede ver ni corregir.
   */
  selectUserType(value: string): void {
    const type: LoginUserType = value === 'member' ? 'member' : 'admin';
    if (type === this.userType()) return;

    this.userType.set(type);
    this.error.set(null);

    const organizationId = this.form.controls.organizationId;
    if (type === 'admin') {
      organizationId.setValue('');
      organizationId.removeValidators(Validators.required);
      organizationId.markAsUntouched();
      this.organizationRequired.set(false);
    } else {
      organizationId.addValidators(Validators.required);
    }
    organizationId.updateValueAndValidity();
  }

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
    // KAN-91. Not required: the field only appears after the API says the address is in more than
    // one workspace, so asking for it up front would teach a new step to people who never need it.
    //
    // KAN-93. PREFILLED WITH THE LAST ONE THAT WORKED IN THIS BROWSER. Forgetting the identifier is
    // what KAN-93's recovery flow cures; this is what stops it happening, and it is the cheaper half
    // — people forget it because they type it once at setup and never see it again. Remembering is
    // automatic rather than a checkbox because the identifier is not a secret: it is printed in every
    // invitation email and shared with everybody in the workspace. The field stays editable and
    // clearing it works, so nobody is stuck with a remembered value.
    organizationId: [LoginComponent.rememberedOrganization()],
  });

  private readonly toast = inject(ToastService);

  constructor() {
    // La pestaña de miembro pide el identificador; si la pantalla abre ya en ella (porque este
    // navegador recuerda uno), la validación tiene que estar puesta desde el primer render.
    if (this.userType() === 'member') {
      this.form.controls.organizationId.addValidators(Validators.required);
      this.form.controls.organizationId.updateValueAndValidity();
    }

    // ★ EL AVISO DE SESIÓN CADUCADA SE MUESTRA AQUÍ, Y SÓLO UNA VEZ. Terminar una sesión recarga el
    // documento (es lo único que garantiza que no sobreviva en memoria nada del tenant anterior), y
    // esa recarga se lleva por delante cualquier toast pintado antes de salir. Quien terminó la
    // sesión deja la marca; esta pantalla la lee, la muestra y la borra — si no la borrara,
    // reaparecería en el siguiente inicio de sesión hablando de una caducidad que no ocurrió.
    try {
      if (sessionStorage.getItem(SESSION_EXPIRED_NOTICE_KEY)) {
        sessionStorage.removeItem(SESSION_EXPIRED_NOTICE_KEY);
        this.toast.show('SESSION.EXPIRED_TOAST', 'error');
      }
    } catch {
      // Sin almacenamiento no hay aviso; la pantalla de acceso funciona igual.
    }
  }

  fieldError(name: string): string {
    const ctrl = this.form.get(name);
    if (!ctrl || !ctrl.invalid || !ctrl.touched) return '';
    if (ctrl.hasError('required')) return 'VALIDATION.REQUIRED';
    if (ctrl.hasError('email')) return 'VALIDATION.EMAIL';
    return 'VALIDATION.INVALID';
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.isSubmitting()) return;

    this.isSubmitting.set(true);
    this.error.set(null);
    this.emailNotConfirmed.set(false);
    this.accountLocked.set(null);

    this.authService.login(this.form.getRawValue()).subscribe({
      next: (result) => {
        // ★ REMEMBERED ONLY AFTER THE SERVER ACCEPTED IT. Storing what was typed would remember
        // typos, and a wrong remembered identifier is worse than none: it makes every later sign-in
        // fail for a reason the field looks innocent about.
        //
        // ★ BEFORE THE 2FA BRANCH, NOT AFTER IT. A challenge means the credentials AND the workspace
        // were already accepted; the second factor is about the person, not the organization. Putting
        // this line below the early return would have quietly excluded everybody with 2FA on — which
        // is disproportionately the administrators this feature is for.
        LoginComponent.rememberOrganization(this.form.getRawValue().organizationId);

        if (result.requiresTwoFactor) {
          void this.router.navigateByUrl('/auth/verify-2fa');
          return;
        }

        this.currentUser.refresh().subscribe(() => {
          const returnUrl = sessionStorage.getItem('wasnie:return-url') ?? '/dashboard';
          sessionStorage.removeItem('wasnie:return-url');
          this.router.navigateByUrl(returnUrl);
        });
      },
      error: (err: HttpErrorResponse) => {
        const message: string = err?.error?.message ?? '';
        const lock = LoginComponent.parseAccountLocked(message);

        if (message === 'ORGANIZATION_REQUIRED') {
          // KAN-91. The password was already accepted; what is missing is WHICH workspace. The field
          // appears now rather than on first load, and the answer names no workspace — that list is
          // private, and returning it would tell whoever holds this password every company the
          // person works for.
          //
          // La pestaña cambia sola a la de miembro: el campo que falta sólo existe ahí, y dejar el
          // aviso en la pestaña de administrador pediría algo que no está en pantalla.
          this.organizationRequired.set(true);
          this.userType.set('member');
          this.form.controls.organizationId.addValidators(Validators.required);
          this.form.controls.organizationId.updateValueAndValidity();
          this.error.set(this.translate.instant('AUTH.ORGANIZATION_REQUIRED'));
        } else if (message === 'EMAIL_NOT_CONFIRMED') {
          this.emailNotConfirmed.set(true);
        } else if (lock !== null) {
          this.accountLocked.set(lock);
        } else {
          this.error.set(message || this.translate.instant('AUTH.INVALID_CREDENTIALS'));
        }
        this.isSubmitting.set(false);
      },
    });
  }

  /**
   * Recognises the one coded message this screen understands, and nothing else.
   * The code is matched against a literal — never used to build a translation key — so an
   * unknown code from the API can only fall through to the generic message, never print an
   * internal identifier at the user.
   */
  private static parseAccountLocked(message: string): { minutes: number | null } | null {
    if (!message.startsWith('ACCOUNT_LOCKED:')) return null;
    const minutes = Number.parseInt(message.slice('ACCOUNT_LOCKED:'.length), 10);
    // A malformed countdown still means the account is locked. Falling through to the generic
    // branch here would print the raw code on screen, which is the one thing it must never do.
    return { minutes: Number.isFinite(minutes) && minutes > 0 ? minutes : null };
  }

  /**
   * The identifier this browser last signed in with, or ''.
   *
   * ★ EVERY ACCESS IS WRAPPED. localStorage throws in a private window, with site data blocked, and
   * in the jsdom runs that mount this component. A sign-in page that cannot render because storage
   * was unavailable would be the worst possible trade for a convenience.
   */
  private static rememberedOrganization(): string {
    try {
      return localStorage.getItem(LoginComponent.LAST_ORGANIZATION_KEY) ?? '';
    } catch {
      return '';
    }
  }

  /**
   * ★ AN EMPTY IDENTIFIER CLEARS THE MEMORY RATHER THAN BEING IGNORED. Somebody who empties the field
   * and signs into their only workspace is saying "stop filling this in", and writing nothing would
   * have left the old value to reappear on the next visit.
   */
  private static rememberOrganization(organizationId: string): void {
    try {
      const slug = organizationId.trim();
      if (slug) localStorage.setItem(LoginComponent.LAST_ORGANIZATION_KEY, slug);
      else localStorage.removeItem(LoginComponent.LAST_ORGANIZATION_KEY);
    } catch {
      // No storage, no memory. Signing in worked, which is the part that matters.
    }
  }

  goToConfirmPending(): void {
    const email = this.form.get('email')?.value ?? '';
    if (email) sessionStorage.setItem('wasnie:confirm-email', email);
    void this.router.navigateByUrl('/auth/confirm-email-pending');
  }
}
