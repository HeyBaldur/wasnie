import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { CurrentUserService } from '../auth/current-user.service';

export const onboardingGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const currentUser = inject(CurrentUserService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return router.createUrlTree(['/auth/login']);
  }

  const user = currentUser.currentUser();
  if (user?.hasSelectedPlan) {
    return router.createUrlTree(['/dashboard']);
  }

  return true;
};
