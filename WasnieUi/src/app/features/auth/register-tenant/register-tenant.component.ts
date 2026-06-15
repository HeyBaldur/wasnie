import { Component, inject, signal, DestroyRef } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../../../core/services/auth.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { ThemeToggleComponent } from '../../../shared/components/theme-toggle/theme-toggle.component';
import { WsInputComponent, WsButtonComponent } from '../../../shared/ui';

function passwordStrength(ctrl: AbstractControl): ValidationErrors | null {
  const v: string = ctrl.value ?? '';
  if (!v) return null;
  if (!/[A-Z]/.test(v)) return { noUppercase: true };
  if (!/[0-9]/.test(v)) return { noDigit: true };
  return null;
}

@Component({
  selector: 'app-register-tenant',
  standalone: true,
  imports: [ReactiveFormsModule, TranslatePipe, RouterLink, ThemeToggleComponent, WsInputComponent, WsButtonComponent],
  templateUrl: './register-tenant.component.html',
  styleUrl: './register-tenant.component.scss',
})
export class RegisterTenantComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);
  private slugUserEdited = false;

  readonly form = this.fb.nonNullable.group({
    tenantName: ['', [Validators.required, Validators.maxLength(200)]],
    tenantSlug: ['', [Validators.required, Validators.pattern(/^[a-z0-9-]+$/)]],
    adminFirstName: ['', Validators.required],
    adminLastName: ['', Validators.required],
    adminEmail: ['', [Validators.required, Validators.email]],
    adminPassword: ['', [Validators.required, Validators.minLength(8), passwordStrength]],
  });

  constructor() {
    this.form.controls.tenantName.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(name => {
        if (!this.slugUserEdited) {
          this.form.controls.tenantSlug.setValue(this.toSlug(name), { emitEvent: false });
        }
      });

    this.form.controls.tenantSlug.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(slug => {
        const derived = this.toSlug(this.form.controls.tenantName.value);
        this.slugUserEdited = slug !== derived && slug !== '';
      });
  }

  private toSlug(value: string): string {
    return value
      .toLowerCase()
      .normalize('NFD').replace(/[̀-ͯ]/g, '')
      .replace(/\s+/g, '-')
      .replace(/[^a-z0-9-]/g, '')
      .replace(/-{2,}/g, '-')
      .replace(/^-+|-+$/g, '');
  }

  fieldError(name: string): string {
    const ctrl = this.form.get(name);
    if (!ctrl || !ctrl.invalid || !ctrl.touched) return '';
    if (ctrl.hasError('required')) return 'VALIDATION.REQUIRED';
    if (ctrl.hasError('email')) return 'VALIDATION.EMAIL';
    if (ctrl.hasError('minlength')) return 'VALIDATION.PASSWORD_MIN';
    if (ctrl.hasError('noUppercase')) return 'VALIDATION.PASSWORD_UPPER';
    if (ctrl.hasError('noDigit')) return 'VALIDATION.PASSWORD_DIGIT';
    if (ctrl.hasError('pattern')) return 'VALIDATION.SLUG_PATTERN';
    return 'VALIDATION.INVALID';
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.isSubmitting()) return;

    this.isSubmitting.set(true);
    this.error.set(null);

    this.authService.registerTenant(this.form.getRawValue()).subscribe({
      next: () => {
        sessionStorage.setItem('wasnie:confirm-email', this.form.controls.adminEmail.value);
        this.currentUser.refresh().subscribe(() =>
          this.router.navigateByUrl('/auth/confirm-email-pending'));
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(err?.error?.message ?? this.translate.instant('ERRORS.GENERIC'));
        this.isSubmitting.set(false);
      },
    });
  }
}
