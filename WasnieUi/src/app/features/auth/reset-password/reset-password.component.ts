import { Component, inject, signal, OnInit } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../../core/services/auth.service';
import { WsInputComponent, WsButtonComponent } from '../../../shared/ui';

function passwordsMatchValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const newPassword = control.get('newPassword')?.value;
    const confirmPassword = control.get('confirmPassword')?.value;
    if (newPassword && confirmPassword && newPassword !== confirmPassword) {
      return { passwordsMismatch: true };
    }
    return null;
  };
}

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [ReactiveFormsModule, TranslatePipe, RouterLink, WsInputComponent, WsButtonComponent],
  templateUrl: './reset-password.component.html',
  styleUrl: './reset-password.component.scss',
})
export class ResetPasswordComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);

  private userId = '';
  private token = '';

  readonly isSubmitting = signal(false);
  readonly errorMsg = signal<string | null>(null);
  readonly state = signal<'form' | 'invalid-link'>('form');

  readonly form = this.fb.nonNullable.group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(10)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: passwordsMatchValidator() }
  );

  ngOnInit(): void {
    this.userId = this.route.snapshot.queryParamMap.get('userId') ?? '';
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';

    if (!this.userId || !this.token) {
      this.state.set('invalid-link');
    }
  }

  fieldError(name: string): string {
    const ctrl = this.form.get(name);
    if (!ctrl || !ctrl.invalid || !ctrl.touched) return '';
    if (ctrl.hasError('required')) return 'VALIDATION.REQUIRED';
    if (ctrl.hasError('minlength')) return 'VALIDATION.MIN_LENGTH';
    return 'VALIDATION.INVALID';
  }

  get passwordsMismatch(): boolean {
    return this.form.hasError('passwordsMismatch') &&
      (this.form.get('confirmPassword')?.touched ?? false);
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.isSubmitting()) return;

    this.isSubmitting.set(true);
    this.errorMsg.set(null);

    this.authService.resetPassword(this.userId, this.token, this.form.getRawValue().newPassword).subscribe({
      next: () => {
        void this.router.navigateByUrl('/auth/login?passwordReset=true');
      },
      error: (err) => {
        this.errorMsg.set(err?.error?.message ?? 'RESET_PASSWORD.INVALID_LINK');
        this.isSubmitting.set(false);
      },
    });
  }
}
