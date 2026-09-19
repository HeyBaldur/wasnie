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

/**
 * The same gate, satisfied by ANY of several permissions (KAN-93, bug 6).
 *
 * ★★ IT EXISTS BECAUSE ONE SCREEN CAN HAVE TWO LEGITIMATE READERS. The plan page is administration
 * for whoever holds `Plans.Read` and a read-only payslip explainer for a rep holding
 * `Plans.ReadOwn` — two permissions, one route. Duplicating the route to give each its own guard
 * would be two URLs for one page, and the second would drift.
 *
 * ★ IT IS A DOOR, NOT THE DECISION. Which PLAN a `Plans.ReadOwn` holder may open is the server's
 * business (`PlanAccessGuard`); this only stops somebody with neither permission reaching the
 * component at all. A router guard runs in the browser and can never be the security boundary.
 */
export function hasAnyPermissionGuard(...permissions: string[]): CanActivateFn {
  return () => {
    const currentUser = inject(CurrentUserService);
    const router = inject(Router);
    return permissions.some(p => currentUser.hasPermission(p))
      ? true
      : router.createUrlTree(['/forbidden']);
  };
}
