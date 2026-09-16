import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { CurrentUserService } from '../auth/current-user.service';

export const qualificationGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const currentUser = inject(CurrentUserService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return router.createUrlTree(['/auth/login']);
  }

  const user = currentUser.currentUser();

  if (user && !user.emailConfirmed) {
    return router.createUrlTree(['/auth/confirm-email-pending']);
  }

  if (user?.isQualified) {
    // KAN-77: straight into the product — the trial needs no plan selection.
    return router.createUrlTree(['/dashboard']);
  }

  return true;
};
