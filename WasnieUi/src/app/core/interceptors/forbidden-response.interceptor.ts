import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ToastService } from '../../shared/services/toast.service';
import { TierLimitModalService } from '../../shared/components/tier-limit-modal/tier-limit-modal.service';

export const forbiddenResponseInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);
  const tierLimitModal = inject(TierLimitModalService);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 403) {
        const body = err.error as {
          error?: string;
          tier?: string;
          currentCount?: number;
          limit?: number;
        } | null;

        if (body?.error === 'TierLimitExceeded') {
          tierLimitModal.show({
            tier: body.tier ?? '',
            currentCount: body.currentCount ?? 0,
            limit: body.limit ?? 0,
          });
        } else {
          toast.show('ERRORS.FORBIDDEN', 'error');
        }
      }
      return throwError(() => err);
    })
  );
};
