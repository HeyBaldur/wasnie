import {
  HttpInterceptorFn,
  HttpRequest,
  HttpHandlerFn,
  HttpErrorResponse,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, take, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { SessionRefreshService } from '../services/session-refresh.service';
import { SessionExitService } from '../services/session-exit.service';

export const errorInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
) => {
  const authService = inject(AuthService);
  const sessionRefresh = inject(SessionRefreshService);
  const sessionExit = inject(SessionExitService);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      // Skip refresh only for the refresh endpoint itself (prevents infinite loop).
      // Other /auth/ endpoints (e.g. /auth/me) should go through the normal refresh cycle.
      if (err.status !== 401 || !authService.isAuthenticated() || req.url.includes('/auth/refresh')) {
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
          // El aviso lo muestra la pantalla de acceso: esta salida recarga el documento y un toast
          // pintado aquí no sobreviviría a la recarga.
          sessionExit.toLogin('expired');
          return throwError(() => refreshErr);
        })
      );
    })
  );
};
