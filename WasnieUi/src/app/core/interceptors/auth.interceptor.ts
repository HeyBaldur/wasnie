import { HttpInterceptorFn, HttpRequest, HttpHandlerFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, of, switchMap } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Attaches the session to every request — and renews it BEFORE it expires (KAN-1).
 *
 * ★★ WHY THE RENEWAL IS HERE AND NOT ONLY IN THE 401 HANDLER. Reacting to a 401 works — the error
 * interceptor refreshes and retries — but it costs the user a failed round trip every time the token
 * ages out, and it is the moment the old code turned into a logout. The happy-path AC asks for the
 * renewal to be invisible, and the only place that can be true is before the request leaves.
 *
 * ★ THE 401 PATH STAYS EXACTLY AS IT IS. This is an optimisation, not a replacement: if the proactive
 * renewal fails for any reason the request still goes out with the old token, gets its 401, and the
 * error interceptor decides — including whether to log the user out. Duplicating that decision here
 * would be two components disagreeing about when a session ends.
 */
export const authInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
) => {
  const authService = inject(AuthService);
  const token = authService.getAccessToken();

  if (!token) {
    return next(req);
  }

  const send = (bearer: string): Observable<unknown> =>
    next(req.clone({
      setHeaders: {
        Authorization: `Bearer ${bearer}`,
        ...(authService.tenantId() ? { 'X-Tenant-Id': authService.tenantId()! } : {}),
      },
    })) as Observable<unknown>;

  // ★ THE RENEWAL CALL ITSELF MUST NEVER TRIGGER A RENEWAL. `refresh()` posts through this same chain,
  //   so without this guard an expiring token would recurse until the stack gave out.
  if (req.url.includes('/auth/refresh') || !authService.accessTokenNeedsRenewal()) {
    return send(token) as ReturnType<HttpInterceptorFn>;
  }

  // ★ ONE RENEWAL PER BROWSER, NOT ONE PER REQUEST. `refresh()` runs inside the cross-tab lock and
  //   re-reads storage, so a burst of expiring requests produces a single network call and the rest
  //   adopt what it wrote. See AuthService.refresh.
  return authService.refresh().pipe(
    catchError(() => of(null)),
    switchMap(() => send(authService.getAccessToken() ?? token))
  ) as ReturnType<HttpInterceptorFn>;
};
