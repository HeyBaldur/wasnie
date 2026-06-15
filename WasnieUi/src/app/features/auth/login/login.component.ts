import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { ThemeToggleComponent } from '../../../shared/components/theme-toggle/theme-toggle.component';
import { WsInputComponent, WsButtonComponent } from '../../../shared/ui';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, TranslatePipe, RouterLink, ThemeToggleComponent, WsInputComponent, WsButtonComponent],
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
  readonly alreadyConfirmed = signal(
    this.route.snapshot.queryParamMap.has('alreadyConfirmed')
  );
  readonly passwordResetSuccess = signal(
    this.route.snapshot.queryParamMap.has('passwordReset')
  );

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

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

    this.authService.login(this.form.getRawValue()).subscribe({
      next: (result) => {
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
        if (err?.error?.message === 'EMAIL_NOT_CONFIRMED') {
          this.emailNotConfirmed.set(true);
        } else {
          this.error.set(err?.error?.message ?? this.translate.instant('AUTH.INVALID_CREDENTIALS'));
        }
        this.isSubmitting.set(false);
      },
    });
  }

  goToConfirmPending(): void {
    const email = this.form.get('email')?.value ?? '';
    if (email) sessionStorage.setItem('wasnie:confirm-email', email);
    void this.router.navigateByUrl('/auth/confirm-email-pending');
  }
}
