import { Component, inject, OnInit, signal } from '@angular/core';
import {
  AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { InvitationsApiService } from '../../users/services/users.api.service';
import { InvitationPreview } from '../../users/models/user.model';
import { WsInputComponent, WsButtonComponent } from '../../../shared/ui';

function passwordsMatchValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const password = control.get('password')?.value;
    const confirmPassword = control.get('confirmPassword')?.value;
    return password && confirmPassword && password !== confirmPassword
      ? { passwordsMismatch: true }
      : null;
  };
}

/**
 * Accepting an invitation (KAN-32). PUBLIC — the reader has no account yet.
 *
 * ★★ IT VALIDATES THE TOKEN BEFORE DRAWING THE FORM. A page that rendered happily and only refused
 * on submit would have the person choose a password, type it twice, and only then learn the link died
 * last Tuesday. The preview call answers the same four refusals the accept call does, so what the
 * screen shows and what the API will do cannot disagree.
 *
 * ★★ THE REFUSAL IS SHOWN BY CODE, NEVER BY THE SERVER'S SENTENCE. This is the one screen whose
 * reader has no language preference on record — there is no account yet — so the browser's locale
 * decides, and a backend-authored English sentence would be the one thing it cannot translate. The
 * keys are whitelisted in `refusalKey`, never built by concatenation (§C2): an unknown code falls
 * back to the generic message rather than printing an internal identifier at a stranger.
 */
@Component({
  selector: 'app-accept-invitation',
  standalone: true,
  imports: [ReactiveFormsModule, TranslatePipe, RouterLink, WsInputComponent, WsButtonComponent],
  templateUrl: './accept-invitation.component.html',
  styleUrl: './accept-invitation.component.scss',
})
export class AcceptInvitationComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(InvitationsApiService);

  private token = '';

  readonly state = signal<'loading' | 'form' | 'invalid' | 'done'>('loading');
  readonly preview = signal<InvitationPreview | null>(null);
  readonly refusal = signal<string | null>(null);
  readonly isSubmitting = signal(false);
  readonly errorMsg = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group(
    {
      firstName: ['', [Validators.required, Validators.maxLength(100)]],
      lastName: ['', [Validators.required, Validators.maxLength(100)]],
      password: ['', [Validators.required, Validators.minLength(10)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: passwordsMatchValidator() },
  );

  async ngOnInit(): Promise<void> {
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';

    if (!this.token) {
      this.refusal.set('INVITATION_TOKEN_INVALID');
      this.state.set('invalid');
      return;
    }

    try {
      this.preview.set(await firstValueFrom(this.api.preview(this.token)));
      this.state.set('form');
    } catch (err: unknown) {
      this.refusal.set(this.codeFrom(err));
      this.state.set('invalid');
    }
  }

  async submit(): Promise<void> {
    if (this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMsg.set(null);

    try {
      const { firstName, lastName, password } = this.form.getRawValue();
      await firstValueFrom(this.api.accept({
        token: this.token,
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        password,
      }));

      this.state.set('done');
    } catch (err: unknown) {
      const code = this.codeFrom(err);

      // A token that died between loading this page and submitting it is a different situation from
      // a password the server would not take: the first has no second attempt, so the form goes away.
      if (code !== null) {
        this.refusal.set(code);
        this.state.set('invalid');
      } else {
        this.errorMsg.set('ACCEPT_INVITATION.ERROR_GENERIC');
      }
    } finally {
      this.isSubmitting.set(false);
    }
  }

  goToLogin(): void {
    void this.router.navigate(['/auth/login']);
  }

  /**
   * The translation key for a refusal code.
   *
   * ★★ AN EXPLICIT WHITELIST, NOT `'ACCEPT_INVITATION.' + code` (§C2). A concatenated key for a code
   * this build does not know would print the raw identifier on screen — at somebody who has never
   * seen the product and cannot tell it from a real message.
   */
  refusalKey(code: string | null): string {
    switch (code) {
      case 'INVITATION_TOKEN_INVALID': return 'ACCEPT_INVITATION.REFUSAL.INVALID';
      case 'INVITATION_TOKEN_ALREADY_USED': return 'ACCEPT_INVITATION.REFUSAL.ALREADY_USED';
      case 'INVITATION_TOKEN_EXPIRED': return 'ACCEPT_INVITATION.REFUSAL.EXPIRED';
      case 'INVITATION_TOKEN_REVOKED': return 'ACCEPT_INVITATION.REFUSAL.REVOKED';
      case 'INVITATION_EMAIL_ALREADY_MEMBER': return 'ACCEPT_INVITATION.REFUSAL.ALREADY_MEMBER';
      default: return 'ACCEPT_INVITATION.REFUSAL.GENERIC';
    }
  }

  /** The coded refusal out of an HTTP error, or null when the failure was not one of ours. */
  private codeFrom(err: unknown): string | null {
    const code = (err as { error?: { code?: unknown } } | null)?.error?.code;
    return typeof code === 'string' ? code : null;
  }
}
