import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { MyDashboard } from '../models/my-dashboard.model';

/**
 * ★ THE CALL CARRIES NO IDENTIFIER. /api/me/dashboard answers about whoever holds the token; there is
 * no parameter here to get wrong, and no version of this request that reads another person's pay.
 */
@Injectable({ providedIn: 'root' })
export class MyDashboardApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/me`;

  get(): Observable<MyDashboard> {
    return this.http.get<MyDashboard>(`${this.base}/dashboard`);
  }
}
