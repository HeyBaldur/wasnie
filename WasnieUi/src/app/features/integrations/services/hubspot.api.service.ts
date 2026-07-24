import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  HubSpotConnectResult,
  HubSpotConnectionStatus,
  HubSpotPingResult,
} from '../models/hubspot.model';
import {
  HubSpotDealsPreview,
  HubSpotImportResult,
  LinkCrmOwnerRequest,
  LinkCrmOwnerResult,
  UnresolvedCrmOwners,
} from '../models/crm-sync.model';

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

  /** WI-CRM-CATEGORY: set (or clear, with null/empty) the HubSpot property that feeds Category. */
  setCategoryProperty(propertyName: string | null): Observable<void> {
    return this.http.put<void>(`${this.base}/category-property`, { propertyName });
  }

  /** Verification-only test call. */
  ping(): Observable<HubSpotPingResult> {
    return this.http.post<HubSpotPingResult>(`${this.base}/ping`, {});
  }

  /** FASE 2a: read-only preview of closed-won deals (creates nothing). */
  previewDeals(): Observable<HubSpotDealsPreview> {
    return this.http.get<HubSpotDealsPreview>(`${this.base}/deals/preview`);
  }

  /** FASE 2c: import closed-won deals as transactions (idempotent). */
  importDeals(): Observable<HubSpotImportResult> {
    return this.http.post<HubSpotImportResult>(`${this.base}/deals/import`, {});
  }

  /** FASE 3: trigger an immediate incremental sync now (in addition to the hourly schedule). */
  syncNow(): Observable<void> {
    return this.http.post<void>(`${this.base}/deals/sync-now`, {});
  }

  /** FASE 2d: owners with closed-won deals that did not auto-resolve to a payee. */
  getUnresolvedOwners(): Observable<UnresolvedCrmOwners> {
    return this.http.get<UnresolvedCrmOwners>(`${this.base}/owners/unresolved`);
  }

  /** FASE 2d: manually link an owner to a payee (optionally reassign their Unassigned transactions). */
  linkOwner(request: LinkCrmOwnerRequest): Observable<LinkCrmOwnerResult> {
    return this.http.post<LinkCrmOwnerResult>(`${this.base}/owners/link`, request);
  }
}
