import { Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { TranslatePipe } from '@ngx-translate/core';
import { environment } from '../../../../environments/environment';

@Component({
  selector: 'app-confirm-email-change',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <div class="confirm-email-change">
      @if (loading()) {
        <p>{{ 'PROFILE.CONFIRM_EMAIL_LOADING' | translate }}</p>
      } @else if (success()) {
        <h2>{{ 'PROFILE.CONFIRM_EMAIL_SUCCESS_TITLE' | translate }}</h2>
        <p>{{ 'PROFILE.CONFIRM_EMAIL_SUCCESS_DESC' | translate }}</p>
      } @else {
        <h2>{{ 'PROFILE.CONFIRM_EMAIL_ERROR_TITLE' | translate }}</h2>
        <p>{{ errorMessage() }}</p>
      }
    </div>
  `,
  styles: [`
    .confirm-email-change {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      padding: 32px;
      text-align: center;
      gap: 16px;
    }
  `],
})
export class ConfirmEmailChangeComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);

  readonly loading = signal(true);
  readonly success = signal(false);
  readonly errorMessage = signal('');

  ngOnInit(): void {
    const userId = this.route.snapshot.queryParamMap.get('userId') ?? '';
    const token = this.route.snapshot.queryParamMap.get('token') ?? '';

    if (!userId || !token) {
      this.loading.set(false);
      this.errorMessage.set('Invalid confirmation link.');
      return;
    }

    const url = `${environment.apiBaseUrl}/profile/confirm-email-change?userId=${encodeURIComponent(userId)}&token=${encodeURIComponent(token)}`;

    this.http.get<{ message: string }>(url).subscribe({
      next: () => {
        this.loading.set(false);
        this.success.set(true);
        // Redirect to login after 3 seconds
        setTimeout(() => void this.router.navigateByUrl('/auth/login'), 3000);
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(err?.error?.message ?? 'This link is invalid or has expired.');
      },
    });
  }
}
