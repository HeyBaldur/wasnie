import { HttpClient } from '@angular/common/http';
import { Injectable, signal, computed, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResult, LoginRequest, RegisterTenantRequest, TokenPair } from '../models/auth.model';
import { TabSyncService } from './tab-sync.service';

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
      .pipe(tap((result) => this.persistSession(result)));
  }

  registerTenant(request: RegisterTenantRequest): Observable<AuthResult> {
    return this.http
      .post<AuthResult>(
        `${environment.apiBaseUrl}/auth/register-tenant`,
        request
      )
      .pipe(tap((result) => this.persistSession(result)));
  }

  refresh(): Observable<TokenPair> {
    const refreshToken = this.getRefreshToken();
    return this.http
      .post<TokenPair>(`${environment.apiBaseUrl}/auth/refresh`, {
        refreshToken,
      })
      .pipe(
        tap((tokens) => {
          const user = this._currentUser();
          if (user) {
            this.persistSession({ ...user, tokens });
          }
        })
      );
  }

  /**
   * Clear auth state without broadcasting to other tabs.
   * Call this when reacting to a remote logout/session-expired signal to avoid re-broadcasting.
   */
  clearSessionSilent(): void {
    this._currentUser.set(null);
    localStorage.removeItem('wasnie_session');
    sessionStorage.removeItem('wasnie:confirm-email');
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
    this.clearSessionSilent();
    if (wasAuthenticated) {
      this.tabSync.broadcast({ type: 'session-expired' });
    }
  }

  getAccessToken(): string | null {
    return this._currentUser()?.tokens.accessToken ?? null;
  }

  getRefreshToken(): string | null {
    return this._currentUser()?.tokens.refreshToken ?? null;
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
