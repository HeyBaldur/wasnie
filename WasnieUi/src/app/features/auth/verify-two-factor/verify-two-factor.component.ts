import { Component, inject, signal, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { ThemeToggleComponent } from '../../../shared/components/theme-toggle/theme-toggle.component';
import { WsInputComponent, WsButtonComponent } from '../../../shared/ui';

@Component({
  selector: 'app-verify-two-factor',
  standalone: true,
  imports: [ReactiveFormsModule, TranslatePipe, ThemeToggleComponent, WsInputComponent, WsButtonComponent],
  templateUrl: './verify-two-factor.component.html',
  styleUrl: './verify-two-factor.component.scss',
})
export class VerifyTwoFactorComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly router = inject(Router);

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly useRecoveryCode = signal(false);
  readonly email = signal('');

  readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required]],
  });

  ngOnInit(): void {
    const challengeEmail = this.authService.getTwoFactorEmail();
    if (!challengeEmail && !sessionStorage.getItem('wasnie:2fa-challenge')) {
      void this.router.navigateByUrl('/auth/login');
      return;
    }
    this.email.set(challengeEmail);
  }

  toggleMode(): void {
    this.useRecoveryCode.set(!this.useRecoveryCode());
    this.form.reset();
    this.error.set(null);
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.isSubmitting()) return;

    this.isSubmitting.set(true);
    this.error.set(null);

    const code = this.form.getRawValue().code.trim();

    this.authService.verifyTwoFactor(code, this.useRecoveryCode()).subscribe({
      next: () => {
        this.currentUser.refresh().subscribe(() => {
          const returnUrl = sessionStorage.getItem('wasnie:return-url') ?? '/dashboard';
          sessionStorage.removeItem('wasnie:return-url');
          void this.router.navigateByUrl(returnUrl);
        });
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(err?.error?.message ?? 'TWO_FACTOR.INVALID_CODE');
        this.isSubmitting.set(false);
      },
    });
  }

  backToLogin(): void {
    sessionStorage.removeItem('wasnie:2fa-challenge');
    sessionStorage.removeItem('wasnie:2fa-email');
    void this.router.navigateByUrl('/auth/login');
  }
}
