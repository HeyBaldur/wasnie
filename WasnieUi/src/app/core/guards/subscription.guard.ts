import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { SubscriptionService } from '../../features/subscription/services/subscription.service';

export const subscriptionGuard: CanActivateFn = () => {
  const subscriptionService = inject(SubscriptionService);
  const router = inject(Router);

  return subscriptionService.getCurrent().pipe(
    map(sub => {
      if (sub.status === 'Canceled') {
        return router.createUrlTree(['/subscription/reactivate']);
      }
      return true;
    }),
    catchError(() => of(true)),
  );
};
