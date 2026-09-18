import { HttpClient, HttpParams } from '@angular/common/http';
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

  /**
   * KAN-98 - the two dates are the only input, and they are not an identifier: they say WHICH DAYS,
   * never WHOSE. The route still answers about whoever holds the token.
   *
   * THE PARAMS ARE BUILT HERE AND NOT THROUGH `buildHttpParams`. That helper dropped `dateFrom`/`dateTo`
   * in silence on the payee page (KAN-63) and the screen showed an unfiltered period with no error
   * anywhere - a range that vanishes on the way out is worse than one that is refused.
   */
  get(from?: string | null, to?: string | null): Observable<MyDashboard> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);

    return this.http.get<MyDashboard>(`${this.base}/dashboard`, { params });
  }
}
