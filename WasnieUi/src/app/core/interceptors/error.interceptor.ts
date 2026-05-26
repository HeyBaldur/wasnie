import {
  HttpInterceptorFn,
  HttpRequest,
  HttpHandlerFn,
  HttpErrorResponse,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, take, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { SessionRefreshService } from '../services/session-refresh.service';
import { ToastService } from '../../shared/services/toast.service';

export const errorInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
) => {
  const authService = inject(AuthService);
  const sessionRefresh = inject(SessionRefreshService);
  const router = inject(Router);
  const toast = inject(ToastService);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status !== 401 || !authService.isAuthenticated() || req.url.includes('/auth/')) {
        return throwError(() => err);
      }

      if (sessionRefresh.isRefreshing) {
        return sessionRefresh.waitForToken$().pipe(
          take(1),
          switchMap(token => {
            if (!token) return throwError(() => err);
            const retried = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
            return next(retried);
          })
        );
      }

      sessionRefresh.isRefreshing = true;
      return authService.refresh().pipe(
        switchMap(tokens => {
          sessionRefresh.isRefreshing = false;
          sessionRefresh.broadcast(tokens.accessToken);
          const retried = req.clone({ setHeaders: { Authorization: `Bearer ${tokens.accessToken}` } });
          return next(retried);
        }),
        catchError(refreshErr => {
          sessionRefresh.isRefreshing = false;
          sessionRefresh.broadcast(null);
          authService.forceLogout(true);
          toast.show('SESSION.EXPIRED_TOAST', 'error');
          router.navigateByUrl('/auth/login');
          return throwError(() => refreshErr);
        })
      );
    })
  );
};
