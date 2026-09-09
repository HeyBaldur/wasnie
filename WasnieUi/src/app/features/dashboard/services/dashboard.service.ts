import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { DashboardSummary } from '../models/dashboard.models';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  /** `from`/`to` are ISO yyyy-MM-dd. Omitting both lets the server apply the whole current month. */
  getSummary(from: string, to: string): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>('/api/dashboard', { params: { from, to } });
  }
}
