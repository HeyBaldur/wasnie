import { computed, inject, Injectable, signal } from '@angular/core';
import { AuthService } from '../../../core/services/auth.service';
import { HubSpotApiService } from './hubspot.api.service';
import { HubSpotConnectionStatus } from '../models/hubspot.model';

/**
 * The tenant's HubSpot connection status, held once per TENANT for the whole session.
 *
 * ★★ IT EXISTS TO STOP THE SIDEBAR JUMPING. The sync banner lives in the sidebar, and the sidebar is
 * destroyed and rebuilt on EVERY navigation — each of the 41 feature templates renders its own
 * `<app-shell>`. While the banner fetched its own status in `ngOnInit`, every single click replayed
 * this sequence: status null → `@if (connected())` false → the banner is not in the DOM → the aside
 * measures short → the response lands → the card appears and the aside grows. That visible snap is
 * the blink, and it also meant a request per navigation for a value that changes about twice a year.
 *
 * ★★ THE CACHE BELONGS TO A TENANT, NOT TO THE BROWSER TAB. Signing in as another account does not reload
 * the app, and this root store outlives the login: cached for the session, the previous tenant's
 * "Connected" was shown to a tenant that had never connected HubSpot. The status is now stamped with the
 * tenant it was read for; a different signed-in tenant sees nothing until its own status arrives, and a
 * response that lands after the tenant changed is dropped.
 *
 * ★ CACHED, NOT POLLED. The status only moves when somebody connects, disconnects or reconnects — all
 * of which happen on the Integrations page, which pushes the fresh value in through {@link set}. So
 * the banner cannot go stale without a screen that knows better having said so.
 */
@Injectable({ providedIn: 'root' })
export class HubSpotStatusStore {
  private readonly api = inject(HubSpotApiService);
  private readonly auth = inject(AuthService);

  private readonly _status = signal<HubSpotConnectionStatus | null>(null);
  /** The tenant `_status` was read for. */
  private readonly statusTenant = signal<string | null>(null);
  /** The tenant a fetch was started (or a status pushed) for. */
  private loadedForTenant: string | null = null;

  /** The status of the SIGNED-IN tenant — never another tenant's cached one. */
  readonly status = computed(() =>
    this.statusTenant() !== null && this.statusTenant() === this.auth.tenantId() ? this._status() : null);

  /** The banner shows for Connected only — never for NeedsReconnect, which is not a working sync. */
  readonly connected = computed(() => this.status()?.status === 'Connected');

  /**
   * Fetch the first time for each tenant and never again for it.
   *
   * ★★ THE SECOND CALL FOR THE SAME TENANT MUST BE SILENT. This is called from the banner's `ngOnInit`,
   * which runs on every navigation; re-fetching here would restore both halves of the defect — the
   * request storm and the jump, because the banner would be reading a value that had gone back to null.
   */
  ensureLoaded(): void {
    const tenant = this.auth.tenantId();
    if (tenant === null || tenant === this.loadedForTenant) return;
    this.loadedForTenant = tenant;

    this.api.getStatus().subscribe({
      next: s => this.accept(tenant, s),
      // ★ Silent: if the status cannot be read the banner simply stays away. A sidebar is the wrong
      // place to report that a decorative banner failed to load.
      error: () => this.accept(tenant, null),
    });
  }

  /** Accept a status a screen has just fetched or just changed — the Integrations page after connect,
   * disconnect, category save or a manual sync. Also marks the store loaded: this IS the fetch. */
  set(status: HubSpotConnectionStatus | null): void {
    const tenant = this.auth.tenantId();
    this.loadedForTenant = tenant;
    this.accept(tenant, status);
  }

  private accept(tenant: string | null, status: HubSpotConnectionStatus | null): void {
    // A response for a tenant that is no longer signed in belongs to nobody on screen.
    if (tenant !== this.auth.tenantId()) return;
    this.statusTenant.set(tenant);
    this._status.set(status);
  }
}
