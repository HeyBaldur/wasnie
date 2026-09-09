import { HttpClient } from '@angular/common/http';
import { Injectable, signal, computed, inject } from '@angular/core';
import { Observable, firstValueFrom, from, map, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResult, LoginRequest, RegisterTenantRequest, TokenPair } from '../models/auth.model';
import { TabSyncService } from './tab-sync.service';

/**
 * Trabajo a medias que una pantalla dejó guardado, barrido por PREFIJO.
 *
 * ★ EL CONTRATO ES LA FORMA DE LA CLAVE, NO UN IMPORT: así este servicio no necesita saber qué
 * pantallas guardan borradores, y no apunta desde el núcleo hacia una feature.
 *
 * ★★ EL ASISTENTE DE IMPORTACIÓN ENTRÓ AQUÍ DESPUÉS, y no guarda un borrador cualquiera: guarda un
 * fichero de payees o de transacciones a medio subir. Sobrevivir al cierre de sesión significaba
 * ofrecerle a la SIGUIENTE empresa que entrara en ese navegador el fichero de la anterior.
 */
const DRAFT_PREFIXES = ['wasnie:draft:', 'wasnie:import-wizard:'] as const;

/**
 * Marcas que pertenecen a UNA PERSONA y no al equipo.
 *
 * ★ SON GLOBALES AL NAVEGADOR AUNQUE DESCRIBAN A UN USUARIO, y por eso hay que borrarlas: si no, la
 * siguiente persona que entre en este equipo hereda que ya vio la bienvenida y que ya descartó el
 * aviso de doble factor — o sea, deja de recibir un aviso de seguridad que nadie le enseñó.
 * Las preferencias del EQUIPO — idioma, tema, sidebar plegado — no están aquí a propósito: no
 * describen a nadie y se quedan.
 */
const PER_USER_LOCAL_KEYS = [
  'wasnie:welcome-seen',
  'wasnie:2fa-reminder-snooze',
  'wasnie:2fa-reminder-dismissed',
] as const;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly tabSync = inject(TabSyncService);

  private readonly _currentUser = signal<AuthResult | null>(
    this.loadUserFromStorage()
  );

  readonly currentUser = this._currentUser.asReadonly();
  readonly isAuthenticated = computed(() => this._currentUser() !== null);
  readonly tenantId = computed(() => this._currentUser()?.tenantId ?? null);

  login(request: LoginRequest): Observable<AuthResult> {
    return this.http
      .post<AuthResult>(`${environment.apiBaseUrl}/auth/login`, request)
      .pipe(tap((result) => {
        if (result.requiresTwoFactor) {
          sessionStorage.setItem('wasnie:2fa-challenge', result.twoFactorChallengeToken ?? '');
          sessionStorage.setItem('wasnie:2fa-email', result.email ?? '');
        } else {
          this.persistSession(result);
        }
      }));
  }

  verifyTwoFactor(code: string, isRecoveryCode = false): Observable<AuthResult> {
    const challengeToken = sessionStorage.getItem('wasnie:2fa-challenge') ?? '';
    return this.http
      .post<AuthResult>(`${environment.apiBaseUrl}/auth/verify-2fa`, {
        challengeToken,
        code,
        isRecoveryCode,
      })
      .pipe(tap((result) => {
        sessionStorage.removeItem('wasnie:2fa-challenge');
        sessionStorage.removeItem('wasnie:2fa-email');
        this.persistSession(result);
      }));
  }

  getTwoFactorEmail(): string {
    return sessionStorage.getItem('wasnie:2fa-email') ?? '';
  }

  registerTenant(request: RegisterTenantRequest): Observable<AuthResult> {
    return this.http
      .post<AuthResult>(
        `${environment.apiBaseUrl}/auth/register-tenant`,
        request
      )
      .pipe(tap((result) => this.persistSession(result)));
  }

  /**
   * Renew the session — ONCE per browser, not once per tab (KAN-1).
   *
   * ★★ THE BUG THIS EXISTS TO END, AND IT IS THE THREE FACTS TOGETHER. (1) The server ROTATES the
   * refresh token: `RefreshTokenCommandHandler` revokes the presented one before issuing the new pair,
   * so a refresh token is single-use. (2) Tokens are deliberately NEVER broadcast between tabs —
   * `TabSyncService` carries `activity | logout | session-expired` and a test forbids any extra field.
   * (3) Nothing reloaded `wasnie_session` when another tab wrote it. So tab B kept the token tab A had
   * already spent; when B's access token expired, B's refresh was rejected, B called `forceLogout`, and
   * the `session-expired` broadcast threw EVERY tab out — including the one whose session was fine.
   *
   * ★★ THE FIX IS THE STANDARD ONE: SINGLE-FLIGHT + DOUBLE-CHECKED READ. The lock makes one tab renew
   * at a time; re-reading storage INSIDE the lock is what makes the losers cheap — by the time they run,
   * the winner has already written a usable pair, so they adopt it and never spend a token at all. The
   * re-read is the half that does the work: a lock without it would just serialise the same collision.
   *
   * ★ AND IT PUTS NO TOKEN ON A CHANNEL. The winner publishes through `localStorage`, which is where the
   * session already lives and is same-origin only. The BroadcastChannel rule and its test stand.
   */
  refresh(): Observable<TokenPair> {
    return from(this.renewSession());
  }

  /**
   * How much slack to leave when judging a stored access token.
   *
   * ★ A TOKEN THAT EXPIRES IN A SECOND IS NOT WORTH ADOPTING: the request it would be attached to comes
   * back 401 and we are round the loop again. Thirty seconds is comfortably longer than a request and
   * far shorter than the fifteen-minute lifetime, so it costs at most one early renewal.
   */
  private static readonly ADOPTION_SKEW_MS = 30_000;

  private async renewSession(): Promise<TokenPair> {
    return this.withRefreshLock(async () => {
      // ★ THE DOUBLE-CHECKED READ. Whoever held the lock before us may have already renewed.
      const stored = this.loadUserFromStorage();

      if (stored?.tokens && this.isStillUsable(stored.tokens)) {
        // Adopt, do not renew: spending our (already revoked) token here is exactly the request that
        // used to fail and log everybody out.
        this._currentUser.set(stored);
        return stored.tokens;
      }

      // ★ THE TOKEN COMES FROM STORAGE FIRST, memory second. Ours may be a generation behind; the one
      // on disk is the newest this browser has.
      const refreshToken = stored?.tokens?.refreshToken ?? this.getRefreshToken();

      const tokens = await firstValueFrom(
        this.http.post<TokenPair>(`${environment.apiBaseUrl}/auth/refresh`, { refreshToken })
      );

      const user = stored ?? this._currentUser();
      if (user) {
        this.persistSession({ ...user, tokens });
      }

      return tokens;
    });
  }

  /**
   * Whether the token this tab holds should be renewed BEFORE the next request goes out (KAN-1).
   *
   * ★★ THE HAPPY-PATH AC, AND IT IS THE DIFFERENCE BETWEEN "SEAMLESS" AND "RECOVERED". Renewing only
   * after a 401 means every fifteen minutes one request fails, is retried, and the user waits two round
   * trips for something that could have cost none. Renewing just BEFORE expiry means they never meet
   * the 401 at all.
   *
   * ★ IT REUSES THE ADOPTION SKEW ON PURPOSE. "Too old to send" and "too old to adopt" are the same
   * judgement about the same token; two numbers here would eventually disagree and open a window where
   * a tab adopts a token it would immediately refuse to use.
   */
  accessTokenNeedsRenewal(): boolean {
    const tokens = this._currentUser()?.tokens;
    return !!tokens?.accessToken && !this.isStillUsable(tokens);
  }

  /** Whether a stored access token has enough life left to be worth adopting. */
  private isStillUsable(tokens: TokenPair): boolean {
    if (!tokens.accessToken || !tokens.accessTokenExpiresAt) return false;

    const expiresAt = Date.parse(tokens.accessTokenExpiresAt);
    return Number.isFinite(expiresAt)
      && expiresAt - Date.now() > AuthService.ADOPTION_SKEW_MS;
  }

  /**
   * Runs `work` with at most one tab inside it at a time.
   *
   * ★ Web Locks IS THE STANDARD MECHANISM for this and it is scoped per origin, so it needs no
   * bookkeeping, no timeout and no stale-lock recovery: the browser releases it if the tab dies.
   *
   * ★ AND THE FALLBACK IS NOT A FAILURE. Without Web Locks the double-checked read still runs, so a
   * losing tab that arrives after the winner has written still adopts instead of renewing. It only
   * loses the guarantee for tabs that collide within the same instant — strictly better than today,
   * where every tab renewed unconditionally.
   */
  private async withRefreshLock<T>(work: () => Promise<T>): Promise<T> {
    const locks = navigator.locks;
    return locks?.request
      ? locks.request('wasnie:token-refresh', work)
      : work();
  }

  /**
   * Clear auth state without broadcasting to other tabs.
   * Call this when reacting to a remote logout/session-expired signal to avoid re-broadcasting.
   */
  /**
   * @param keepDrafts
   * Whether the user's unsent work survives.
   *
   * ★★ THE DISTINCTION IS VOLUNTARY VERSUS INVOLUNTARY, AND IT WAS MISSING (KAN-1). Signing out is a
   * decision: nothing should be left on a shared machine, and that argument — made in
   * `clearFeatureDrafts` — still stands. A session EXPIRING is not a decision. Wiping a half-written
   * manual adjustment because a token aged out destroys work the user never chose to abandon, which is
   * precisely the loss this ticket is about; the AC asks for the draft to be kept, and it was being
   * deleted on the very path that expires the session.
   *
   * ★ IT RIDES ON `preserveState`, WHICH ALREADY MEANT THIS. That flag is false exactly where the user
   * acted (the sign-out button) and true exactly where the system acted (idle timer, refresh failure),
   * so the two facts cannot drift apart into two flags that disagree.
   */
  clearSessionSilent(keepDrafts = false): void {
    this._currentUser.set(null);
    localStorage.removeItem('wasnie_session');
    sessionStorage.removeItem('wasnie:confirm-email');
    if (!keepDrafts) {
      AuthService.clearFeatureDrafts();
    }
  }

  /**
   * Unsent text a feature was holding for this user.
   *
   * ★ SWEPT BY PREFIX, so this file needs to know nothing about which features keep drafts — the
   * contract is the shape of the key, not an import. Reaching into the assistant from here would point
   * a core service at a feature, which is the dependency direction the architecture forbids.
   *
   * ★ AND IT MATTERS EVEN THOUGH THE KEYS ALREADY CARRY THE USER. Those keys stop the NEXT user from
   * READING this one's drafts; this stops them from still being there at all. Signing out on a shared
   * machine should leave nothing behind, not merely something addressed to somebody else.
   */
  private static clearFeatureDrafts(): void {
    try {
      Object.keys(sessionStorage)
        .filter((key) => DRAFT_PREFIXES.some((prefix) => key.startsWith(prefix)))
        .forEach((key) => sessionStorage.removeItem(key));

      PER_USER_LOCAL_KEYS.forEach((key) => localStorage.removeItem(key));
    } catch {
      // Storage being unavailable is not a reason a logout can fail.
    }
  }

  /**
   * Voluntary logout — clears session and notifies other tabs ('logout' signal).
   * Other tabs will silently redirect to login without showing a session-expired toast.
   */
  logout(): void {
    const wasAuthenticated = this.isAuthenticated();
    this.clearSessionSilent();
    if (wasAuthenticated) {
      this.tabSync.broadcast({ type: 'logout' });
    }
  }

  /**
   * Forced session termination (inactivity timer, 401 refresh failure).
   * Optionally preserves the current URL as return-URL for post-login redirect.
   * Broadcasts 'session-expired' so other tabs show the expiry toast and redirect.
   */
  forceLogout(preserveState = true): void {
    const wasAuthenticated = this.isAuthenticated();
    if (preserveState) {
      const returnUrl = window.location.pathname + window.location.search;
      if (returnUrl.length > 1 && !returnUrl.startsWith('/auth')) {
        sessionStorage.setItem('wasnie:return-url', returnUrl);
      }
    }
    // The same flag that keeps the return URL keeps the draft: both exist so the user comes back to
    // where they were, and half of "where they were" is what they had typed.
    this.clearSessionSilent(preserveState);
    if (wasAuthenticated) {
      this.tabSync.broadcast({ type: 'session-expired' });
    }
  }

  requestPasswordReset(email: string): Observable<void> {
    return this.http
      .post<unknown>(`${environment.apiBaseUrl}/auth/request-password-reset`, { email })
      .pipe(map(() => undefined));
  }

  resetPassword(userId: string, token: string, newPassword: string): Observable<void> {
    return this.http
      .post<unknown>(`${environment.apiBaseUrl}/auth/reset-password`, { userId, token, newPassword })
      .pipe(map(() => undefined));
  }

  getAccessToken(): string | null {
    return this._currentUser()?.tokens?.accessToken ?? null;
  }

  getRefreshToken(): string | null {
    return this._currentUser()?.tokens?.refreshToken ?? null;
  }

  private persistSession(result: AuthResult): void {
    this._currentUser.set(result);
    localStorage.setItem('wasnie_session', JSON.stringify(result));
  }

  private loadUserFromStorage(): AuthResult | null {
    try {
      const raw = localStorage.getItem('wasnie_session');
      return raw ? (JSON.parse(raw) as AuthResult) : null;
    } catch {
      return null;
    }
  }
}
