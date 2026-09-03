import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { HttpParams } from '@angular/common/http';
import { ReconciliationFilter, ReconciliationPage } from '../models/reconciliation.model';

/**
 * ★ ONLY THE SET KEYS TRAVEL. A null sent as the string "null" is a filter the server would try to
 * match, and "no reason selected" would quietly become "the reason literally called null".
 */
function params(values: Record<string, string | number | null | undefined>): HttpParams {
  let p = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== null && value !== undefined && value !== '') p = p.set(key, String(value));
  }
  return p;
}

/**
 * ★ THE SERVICE OWNS THE HTTP, THE COMPONENT NEVER TOUCHES IT. Same rule as everywhere else in this
 * app; a component that injects HttpClient is a component that has started keeping its own copy of
 * the API contract.
 */
@Injectable({ providedIn: 'root' })
export class ReconciliationApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/reconciliation';

  list(filter: ReconciliationFilter): Observable<ReconciliationPage> {
    return this.http.get<ReconciliationPage>(this.base, {
      params: params({
        payeeId: filter.payeeId,
        reason: filter.reason,
        from: filter.from,
        to: filter.to,
        page: filter.page,
        pageSize: filter.pageSize,
      }),
    });
  }

  /** The vocabulary the filter offers. Served by the API so a new engine reason is filterable at once. */
  reasons(): Observable<string[]> {
    return this.http.get<string[]>(`${this.base}/reasons`);
  }

  /**
   * ★ NO PAGE, NO PAGE SIZE. An export is the whole filtered set; shipping page one under a name
   * that says "reconciliation" would be the worst kind of wrong, because it looks complete.
   */
  exportToExcel(filter: ReconciliationFilter): Observable<Blob> {
    const { payeeId, reason, from, to } = filter;
    return this.http.get(`${this.base}/export`, {
      params: params({ payeeId, reason, from, to }),
      responseType: 'blob' as const,
    });
  }
}
