import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { CurrentUserService } from '../auth/current-user.service';

export const planGuard: CanActivateFn = () => {
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

  if (user && !user.isQualified) {
    return router.createUrlTree(['/onboarding/qualify']);
  }

  // KAN-77: no plan to choose before entering — every new account starts in a free trial. Whether the
  // account may use the product (trial / paying / locked) is subscriptionGuard's question, not this one.

  return true;
};
