import { computed, inject, Injectable, signal } from '@angular/core';
import { HubSpotApiService } from './hubspot.api.service';
import { HubSpotConnectionStatus } from '../models/hubspot.model';

/**
 * The tenant's HubSpot connection status, held once for the whole session.
 *
 * ★★ IT EXISTS TO STOP THE SIDEBAR JUMPING. The sync banner lives in the sidebar, and the sidebar is
 * destroyed and rebuilt on EVERY navigation — each of the 41 feature templates renders its own
 * `<app-shell>`. While the banner fetched its own status in `ngOnInit`, every single click replayed
 * this sequence: status null → `@if (connected())` false → the banner is not in the DOM → the aside
 * measures short → the response lands → the card appears and the aside grows. That visible snap is
 * the blink, and it also meant a request per navigation for a value that changes about twice a year.
 *
 * ★ CACHED, NOT POLLED. The status only moves when somebody connects, disconnects or reconnects — all
 * of which happen on the Integrations page, which pushes the fresh value in through {@link set}. So
 * the banner cannot go stale without a screen that knows better having said so.
 */
@Injectable({ providedIn: 'root' })
export class HubSpotStatusStore {
  private readonly api = inject(HubSpotApiService);

  private readonly _status = signal<HubSpotConnectionStatus | null>(null);
  private loaded = false;

  readonly status = this._status.asReadonly();

  /** The banner shows for Connected only — never for NeedsReconnect, which is not a working sync. */
  readonly connected = computed(() => this._status()?.status === 'Connected');

  /**
   * Fetch the first time and never again.
   *
   * ★★ THE SECOND CALL MUST BE SILENT. This is called from the banner's `ngOnInit`, which runs on
   * every navigation; re-fetching here would restore both halves of the defect — the request storm and
   * the jump, because the banner would be reading a value that had gone back to null.
   */
  ensureLoaded(): void {
    if (this.loaded) return;
    this.loaded = true;

    this.api.getStatus().subscribe({
      next: s => this._status.set(s),
      // ★ Silent: if the status cannot be read the banner simply stays away. A sidebar is the wrong
      // place to report that a decorative banner failed to load.
      error: () => this._status.set(null),
    });
  }

  /** Accept a status a screen has just fetched or just changed — the Integrations page after connect,
   * disconnect, category save or a manual sync. Also marks the store loaded: this IS the fetch. */
  set(status: HubSpotConnectionStatus | null): void {
    this.loaded = true;
    this._status.set(status);
  }
}
