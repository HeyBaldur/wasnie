import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CurrentUserService } from '../current-user.service';

export function hasPermissionGuard(permission: string): CanActivateFn {
  return () => {
    const currentUser = inject(CurrentUserService);
    const router = inject(Router);
    return currentUser.hasPermission(permission)
      ? true
      : router.createUrlTree(['/forbidden']);
  };
}
