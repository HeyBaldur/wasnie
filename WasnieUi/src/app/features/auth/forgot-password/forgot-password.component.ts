import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../../core/services/auth.service';
import { WsInputComponent, WsButtonComponent } from '../../../shared/ui';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [ReactiveFormsModule, TranslatePipe, RouterLink, WsInputComponent, WsButtonComponent],
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.scss',
})
export class ForgotPasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);

  readonly isSubmitting = signal(false);
  readonly sent = signal(false);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
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

    this.authService.requestPasswordReset(this.form.getRawValue().email).subscribe({
      next: () => {
        this.sent.set(true);
        this.isSubmitting.set(false);
      },
      error: () => {
        // Always show success (anti-enumeration — backend returns 200 for all cases).
        this.sent.set(true);
        this.isSubmitting.set(false);
      },
    });
  }
}
