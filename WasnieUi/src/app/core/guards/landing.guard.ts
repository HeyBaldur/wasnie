import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { CurrentUserService } from '../auth/current-user.service';

/**
 * KAN-92 — where a session lands.
 *
 * ★★ THE COMPANY DASHBOARD IS NOT EVERYONE'S HOME. Its figures need Reports.ViewAll, so somebody
 * without it used to arrive at a screen of zeros and conclude the product was broken, or that they
 * had earned nothing. The permission decides the destination, not the role: a fifth role added later
 * lands correctly without anybody remembering to edit a list of names here.
 *
 * ★ IT IS SAFE TO READ THE PERMISSION SYNCHRONOUSLY. The current user is fetched in an app
 * initializer (app.config.ts), so it is present before any route resolves — the same assumption every
 * hasPermissionGuard in this app already makes.
 */
export function landingRedirect(): string {
  return inject(CurrentUserService).hasPermission('Reports.ViewAll')
    ? '/dashboard'
    : '/my-dashboard';
}

/**
 * Guards the company dashboard itself.
 *
 * ★ IT REDIRECTS RATHER THAN FORBIDS. A rep following an old link or a bookmark has done nothing
 * wrong, and /forbidden would tell them they are not welcome in their own product. They are sent to
 * the screen that answers their question instead.
 */
export const companyDashboardGuard: CanActivateFn = (): boolean | UrlTree => {
  const currentUser = inject(CurrentUserService);
  const router = inject(Router);

  return currentUser.hasPermission('Reports.ViewAll')
    ? true
    : router.createUrlTree(['/my-dashboard']);
};
