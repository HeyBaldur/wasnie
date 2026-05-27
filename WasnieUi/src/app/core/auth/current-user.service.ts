import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CurrentUser } from './current-user.model';

@Injectable({ providedIn: 'root' })
export class CurrentUserService {
  private readonly http = inject(HttpClient);

  private readonly _user = signal<CurrentUser | null>(null);
  private readonly _loaded = signal(false);

  readonly currentUser = this._user.asReadonly();
  readonly isLoaded = this._loaded.asReadonly();
  readonly tier = computed(() => this._user()?.tier ?? null);

  hasPermission(permission: string): boolean {
    return this._user()?.permissions.includes(permission) ?? false;
  }

  refresh(): Observable<void> {
    return this.http.get<CurrentUser>(`${environment.apiBaseUrl}/auth/me`).pipe(
      tap(user => {
        this._user.set(user);
        this._loaded.set(true);
      }),
      map(() => undefined),
      catchError(() => {
        this._loaded.set(true);
        return of(undefined);
      })
    );
  }

  clear(): void {
    this._user.set(null);
    this._loaded.set(false);
  }
}
