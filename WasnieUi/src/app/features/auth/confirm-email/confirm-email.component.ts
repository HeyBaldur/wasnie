import { Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { TranslatePipe } from '@ngx-translate/core';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { AuthService } from '../../../core/services/auth.service';
import { WsButtonComponent } from '../../../shared/ui';

@Component({
  selector: 'app-confirm-email',
  standalone: true,
  imports: [TranslatePipe, WsButtonComponent],
  templateUrl: './confirm-email.component.html',
  styleUrl: './confirm-email.component.scss',
})
export class ConfirmEmailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly currentUser = inject(CurrentUserService);
  private readonly authService = inject(AuthService);

  readonly state = signal<'confirming' | 'success' | 'error'>('confirming');
  readonly errorMsg = signal<string | null>(null);

  ngOnInit(): void {
    const userId = this.route.snapshot.queryParamMap.get('userId') ?? '';
    const token = this.route.snapshot.queryParamMap.get('token') ?? '';

    if (!userId || !token) {
      this.state.set('error');
      this.errorMsg.set('CONFIRM_EMAIL.INVALID_LINK');
      return;
    }

    this.http
      .post(`/api/auth/confirm-email?userId=${encodeURIComponent(userId)}&token=${encodeURIComponent(token)}`, {})
      .subscribe({
        next: () => {
          this.state.set('success');
          sessionStorage.removeItem('wasnie:confirm-email');
          // If already authenticated, refresh user so guards pick up emailConfirmed=true.
          if (this.authService.isAuthenticated()) {
            this.currentUser.refresh().subscribe(() => {
              setTimeout(() => void this.router.navigateByUrl('/onboarding/qualify'), 1500);
            });
          } else {
            setTimeout(() => void this.router.navigateByUrl('/auth/login'), 2000);
          }
        },
        error: (err) => {
          this.state.set('error');
          this.errorMsg.set(err?.error?.message ?? 'CONFIRM_EMAIL.ERROR_GENERIC');
        },
      });
  }

  goToLogin(): void {
    void this.router.navigateByUrl('/auth/login');
  }

  requestNew(): void {
    void this.router.navigateByUrl('/auth/confirm-email-pending');
  }
}
