import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { DashboardSummary } from '../models/dashboard.models';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  getSummary(period = 'this-month'): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>('/api/dashboard', { params: { period } });
  }
}
