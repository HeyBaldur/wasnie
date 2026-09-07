import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AuditLogDetail,
  AuditLogFilter,
  AuditLogFilterOptions,
  AuditLogPage,
} from '../models/audit-log.model';

/**
 * ★ ONLY THE SET KEYS TRAVEL. A null sent as the string "null" is a filter the server would try to
 * match, and "no action selected" would quietly become "the action literally called null".
 */
function params(values: Record<string, string | number | null | undefined>): HttpParams {
  let p = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== null && value !== undefined && value !== '') p = p.set(key, String(value));
  }
  return p;
}

/**
 * ★ THE SERVICE OWNS THE HTTP, THE COMPONENT NEVER TOUCHES IT (§5.7).
 */
@Injectable({ providedIn: 'root' })
export class AuditLogApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/audit-logs';

  list(filter: AuditLogFilter): Observable<AuditLogPage> {
    return this.http.get<AuditLogPage>(this.base, {
      params: params({
        action: filter.action,
        // ★ The system actor is already a word (`__system__`), not '', precisely so it survives the
        // empty-value drop above. See AuditLogFilter.actor.
        actor: filter.actor,
        from: filter.from,
        to: filter.to,
        page: filter.page,
        pageSize: filter.pageSize,
      }),
    });
  }

  /** The vocabulary of both dropdowns, read from the log itself. */
  options(): Observable<AuditLogFilterOptions> {
    return this.http.get<AuditLogFilterOptions>(`${this.base}/options`);
  }

  /**
   * The evidence behind one row.
   *
   * ★ FETCHED ON DEMAND, NOT WITH THE PAGE. The stored payloads are unbounded text; carrying them
   * for 25 rows to show one would tie the page's weight to its noisiest action.
   */
  detail(id: number): Observable<AuditLogDetail> {
    return this.http.get<AuditLogDetail>(`${this.base}/${id}`);
  }
}
