import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { FavoriteEntityType, FavoriteItem } from '../models/favorite.model';

/** The one HTTP surface of favorites, for every entity type (KAN-64). */
@Injectable({ providedIn: 'root' })
export class FavoritesApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/favorites';

  list(type: FavoriteEntityType): Observable<FavoriteItem[]> {
    return this.http.get<FavoriteItem[]>(`${this.base}/${type}`);
  }

  add(type: FavoriteEntityType, entityId: string): Observable<void> {
    return this.http.put<void>(`${this.base}/${type}/${encodeURIComponent(entityId)}`, null);
  }

  remove(type: FavoriteEntityType, entityId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${type}/${encodeURIComponent(entityId)}`);
  }
}
