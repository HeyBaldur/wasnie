import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  HubSpotConnectResult,
  HubSpotConnectionStatus,
  HubSpotPingResult,
} from '../models/hubspot.model';

@Injectable({ providedIn: 'root' })
export class HubSpotApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/integrations/hubspot';

  getStatus(): Observable<HubSpotConnectionStatus> {
    return this.http.get<HubSpotConnectionStatus>(`${this.base}/status`);
  }

  /** Returns the HubSpot authorization URL the browser should navigate to. */
  connect(): Observable<HubSpotConnectResult> {
    return this.http.post<HubSpotConnectResult>(`${this.base}/connect`, {});
  }

  disconnect(): Observable<void> {
    return this.http.post<void>(`${this.base}/disconnect`, {});
  }

  /** Verification-only test call. */
  ping(): Observable<HubSpotPingResult> {
    return this.http.post<HubSpotPingResult>(`${this.base}/ping`, {});
  }
}
