import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { SubscriptionService } from '../../features/subscription/services/subscription.service';

/**
 * KAN-77 — a locked account (trial ended, or its subscription ended) sees the paywall instead of the product.
 *
 * ★ FAILS OPEN ON PURPOSE, and it is safe to: this guard only decides WHERE to send the user. The server is the
 * authority — every functional endpoint answers 402 for a locked account (SubscriptionEnforcementMiddleware), and
 * `paymentRequiredInterceptor` turns that 402 into the same redirect. An unreachable access endpoint must not
 * lock a paying customer out of the app.
 */
export const subscriptionGuard: CanActivateFn = () => {
  const subscriptionService = inject(SubscriptionService);
  const router = inject(Router);

  return subscriptionService.getAccess().pipe(
    map(access => (access.state === 'Locked' ? router.createUrlTree(['/billing/paywall']) : true)),
    catchError(() => of(true)),
  );
};
