import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

/** The code the paywall middleware sends with its 402 (SubscriptionEnforcementMiddleware.LockedCode). */
export const ACCOUNT_LOCKED_CODE = 'account_locked';

/**
 * KAN-77 — the server's paywall, reflected in the client. When any request comes back 402 `account_locked`
 * (the trial ended while the tab was open, a cancellation landed), the user is taken to the paywall screen
 * instead of watching pages fail one by one. The error still propagates, so callers behave as before.
 */
export const paymentRequiredInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 402 && err.error?.code === ACCOUNT_LOCKED_CODE && !router.url.startsWith('/billing/paywall')) {
        void router.navigateByUrl('/billing/paywall');
      }
      return throwError(() => err);
    }),
  );
};
