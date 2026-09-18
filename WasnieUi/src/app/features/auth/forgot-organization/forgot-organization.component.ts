import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../../core/services/auth.service';
import { WsInputComponent, WsButtonComponent } from '../../../shared/ui';
import { ThemeToggleComponent } from '../../../shared/components/theme-toggle/theme-toggle.component';
import { LanguageToggleComponent } from '../../../shared/components/language-toggle/language-toggle.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';

/**
 * "I forgot my Organization identifier" (KAN-93).
 *
 * ★★ IT CLOSES THE ONE DOOR WITH NO KEY UNDER THE MAT. A forgotten password has a reset link; a
 * forgotten Organization identifier had nothing at all, and the identifier is a required half of the
 * sign-in form for anybody in more than one workspace. An administrator who could not remember theirs
 * was locked out of their own tenant permanently, with the data sitting on the other side of a field
 * they could not fill in.
 *
 * ★★ THE ANSWER NEVER APPEARS ON THIS SCREEN. It goes to the mailbox, and the confirmation says only
 * that a message may have been sent. Printing it here would make the page a directory of every
 * company using Incentra: type addresses, read the identifiers back. The mailbox is the proof of
 * identity, exactly as it is for a password reset.
 *
 * ★★ THE ERROR PATH SHOWS SUCCESS, DELIBERATELY. The server answers 200 for every case it handles, so
 * a visible failure could only come from the network — and distinguishing it would hand the caller a
 * signal the endpoint spent its whole design avoiding. Mirrors ForgotPasswordComponent.
 *
 * ★ THE SENT SCREEN NAMES THE ADMIN-ONLY RULE. Only administrators are mailed the identifier, so
 * somebody who is not one would otherwise wait for a message that is never coming. Saying it here is
 * the difference between a rule and a silence (§C3).
 */
@Component({
  selector: 'app-forgot-organization',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TranslatePipe,
    RouterLink,
    ThemeToggleComponent,
    LanguageToggleComponent,
    IconComponent,
    WsInputComponent,
    WsButtonComponent,
  ],
  templateUrl: './forgot-organization.component.html',
  styleUrl: '../forgot-password/forgot-password.component.scss',
})
export class ForgotOrganizationComponent {
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

    this.authService.requestOrganizationIdentifier(this.form.getRawValue().email).subscribe({
      next: () => {
        this.sent.set(true);
        this.isSubmitting.set(false);
      },
      error: () => {
        // Always the same screen — see the class comment. The backend returns 200 for every case it
        // handles, so branching here could only distinguish a network failure, at the cost of the
        // property the endpoint exists to have.
        this.sent.set(true);
        this.isSubmitting.set(false);
      },
    });
  }
}
