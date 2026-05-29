import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-forbidden',
  standalone: true,
  imports: [RouterLink, TranslatePipe],
  templateUrl: './forbidden.component.html',
  styleUrl: './forbidden.component.scss',
})
export class ForbiddenComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  constructor() {
    // If we land here without a valid session (e.g. token expired and not yet cleared),
    // send straight to login rather than showing a forbidden page the user can bypass.
    if (!this.authService.isAuthenticated()) {
      this.router.navigateByUrl('/auth/login');
    }
  }
}
