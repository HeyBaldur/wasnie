import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ManualContent, ManualStatus } from '../models/manual.model';

/**
 * The manual is fetched as a BLOB through HttpClient, and that is not a style choice.
 *
 * ★ AN <iframe src="/api/manual/pdf"> WOULD ARRIVE UNAUTHENTICATED. The session is a JWT in
 * localStorage that an interceptor attaches as a request header; the browser does not run interceptors
 * for iframe, embed or object loads, so that request would carry no token and the endpoint would
 * correctly refuse it. Fetching the bytes here — through the same pipeline as every other call — is what
 * lets the viewer show a document that is genuinely behind the login.
 */
@Injectable({ providedIn: 'root' })
export class ManualApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/manual';

  /** Cheap check: is a manual installed at all? Avoids downloading megabytes to find out it is missing. */
  getStatus(): Observable<ManualStatus> {
    return this.http.get<ManualStatus>(`${this.base}/status`);
  }

  /**
   * The manual as markdown — the source of truth, and the same document the assistant answers from.
   *
   * This is what the screen renders. `getPdf` below is the printable export, fetched only when someone
   * asks for it.
   */
  getContent(): Observable<ManualContent> {
    return this.http.get<ManualContent>(`${this.base}/content`);
  }

  getPdf(): Observable<Blob> {
    return this.http.get(`${this.base}/pdf`, { responseType: 'blob' as const });
  }
}
