import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import {
  WsPageLayoutComponent,
  WsCardComponent,
  WsButtonComponent,
  WsBadgeComponent,
  type BadgeVariant,
} from '../../shared/ui';
import { ToastService } from '../../shared/services/toast.service';
import { DateFormatPipe } from '../../shared/pipes/date-format.pipe';
import { RelativeTimePipe } from '../../shared/pipes/relative-time.pipe';
import { HubSpotApiService } from './services/hubspot.api.service';
import { HubSpotConnectionStatus, HubSpotStatus } from './models/hubspot.model';

@Component({
  selector: 'app-integrations',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    TranslatePipe,
    WsPageLayoutComponent,
    WsCardComponent,
    WsButtonComponent,
    WsBadgeComponent,
    DateFormatPipe,
    RelativeTimePipe,
  ],
  templateUrl: './integrations.component.html',
  styleUrl: './integrations.component.scss',
})
export class IntegrationsComponent implements OnInit {
  private readonly api = inject(HubSpotApiService);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly status = signal<HubSpotConnectionStatus | null>(null);

  readonly connecting = signal(false);
  readonly disconnecting = signal(false);
  readonly testing = signal(false);
  // Inline result of the last "Test connection" run (cleared on reload / disconnect).
  readonly testResult = signal<{ ok: boolean; message: string } | null>(null);

  // Phase 2 deal-sync state. syncResult carries a translation key + optional params (counts).
  readonly previewing = signal(false);
  readonly importing = signal(false);
  readonly syncResult = signal<{ ok: boolean; key: string; params?: Record<string, unknown> } | null>(null);

  // Phase 3 "Sync now" (on-demand trigger of the automatic incremental sync).
  readonly syncingNow = signal(false);

  readonly currentStatus = computed<HubSpotStatus>(() => this.status()?.status ?? 'NeverConnected');
  readonly isConnected = computed(() => this.currentStatus() === 'Connected');
  readonly needsReconnect = computed(() => this.currentStatus() === 'NeedsReconnect');
  readonly isDisconnected = computed(() => this.currentStatus() === 'Disconnected');
  readonly neverConnected = computed(() => this.currentStatus() === 'NeverConnected');

  readonly statusBadgeVariant = computed<BadgeVariant>(() => {
    switch (this.currentStatus()) {
      case 'Connected': return 'success';
      case 'NeedsReconnect': return 'warning';
      default: return 'neutral';
    }
  });

  readonly statusLabelKey = computed(() => `INTEGRATIONS.HUBSPOT.STATUS_${this.currentStatus().toUpperCase()}`);

  ngOnInit(): void {
    // The backend OAuth callback redirects here with ?hubspot=connected|error — surface a toast.
    const outcome = this.route.snapshot.queryParamMap.get('hubspot');
    if (outcome === 'connected') {
      this.toast.show('INTEGRATIONS.HUBSPOT.TOAST_CONNECTED', 'success');
      this.clearQueryParam();
    } else if (outcome === 'error') {
      this.toast.show('INTEGRATIONS.HUBSPOT.TOAST_CONNECT_ERROR', 'error');
      this.clearQueryParam();
    }
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.testResult.set(null);
    this.syncResult.set(null);
    this.api.getStatus().subscribe({
      next: s => { this.status.set(s); this.loading.set(false); },
      error: () => { this.loading.set(false); this.loadError.set(true); },
    });
  }

  /** Starts (or restarts, for reconnect) the OAuth flow by navigating the browser to HubSpot. */
  connect(): void {
    this.connecting.set(true);
    this.api.connect().subscribe({
      next: ({ authorizationUrl }) => {
        // Full-page navigation to HubSpot's consent screen.
        window.location.assign(authorizationUrl);
      },
      error: err => {
        this.connecting.set(false);
        this.toast.show(err?.error?.message ?? 'INTEGRATIONS.HUBSPOT.TOAST_CONNECT_ERROR', 'error');
      },
    });
  }

  disconnect(): void {
    this.disconnecting.set(true);
    this.testResult.set(null);
    this.api.disconnect().subscribe({
      next: () => {
        this.disconnecting.set(false);
        this.toast.show('INTEGRATIONS.HUBSPOT.TOAST_DISCONNECTED', 'success');
        this.load();
      },
      error: err => {
        this.disconnecting.set(false);
        this.toast.show(err?.error?.message ?? 'INTEGRATIONS.HUBSPOT.TOAST_DISCONNECT_ERROR', 'error');
      },
    });
  }

  test(): void {
    this.testing.set(true);
    this.testResult.set(null);
    this.api.ping().subscribe({
      next: () => {
        this.testing.set(false);
        // Inline, contextual feedback (clearer than a transient toast for a verification check).
        this.testResult.set({ ok: true, message: 'INTEGRATIONS.HUBSPOT.TEST_HEALTHY' });
      },
      error: err => {
        this.testing.set(false);
        this.testResult.set({ ok: false, message: err?.error?.message ?? 'INTEGRATIONS.HUBSPOT.TEST_FAIL' });
      },
    });
  }

  /** FASE 2a verification: read-only preview of how many closed-won deals HubSpot returns. */
  preview(): void {
    this.previewing.set(true);
    this.syncResult.set(null);
    this.api.previewDeals().subscribe({
      next: (r) => {
        this.previewing.set(false);
        this.syncResult.set({ ok: true, key: 'INTEGRATIONS.HUBSPOT.SYNC.PREVIEW_RESULT', params: { count: r.count } });
      },
      error: (err) => {
        this.previewing.set(false);
        this.syncResult.set({ ok: false, key: err?.error?.message ?? 'INTEGRATIONS.HUBSPOT.SYNC.ERROR' });
      },
    });
  }

  /** FASE 2c: import closed-won deals as transactions (idempotent). */
  import(): void {
    this.importing.set(true);
    this.syncResult.set(null);
    this.api.importDeals().subscribe({
      next: (r) => {
        this.importing.set(false);
        this.syncResult.set({
          ok: true,
          key: 'INTEGRATIONS.HUBSPOT.SYNC.IMPORT_RESULT',
          params: {
            created: r.created,
            assigned: r.assignedToPayee,
            unassigned: r.unassigned,
            skipped: r.skippedAlreadyImported,
          },
        });
      },
      error: (err) => {
        this.importing.set(false);
        this.syncResult.set({ ok: false, key: err?.error?.message ?? 'INTEGRATIONS.HUBSPOT.SYNC.ERROR' });
      },
    });
  }

  /** FASE 3: trigger an immediate incremental sync. The job runs in the background; we refresh the
   * status shortly after so "Last synced" updates once it finishes. */
  syncNow(): void {
    this.syncingNow.set(true);
    this.syncResult.set(null);
    this.api.syncNow().subscribe({
      next: () => {
        this.syncingNow.set(false);
        this.toast.show('INTEGRATIONS.HUBSPOT.SYNC.SYNC_NOW_STARTED', 'success');
        // The sync runs asynchronously; reload status after a short delay to pick up the new last-synced time.
        setTimeout(() => this.load(), 5000);
      },
      error: (err) => {
        this.syncingNow.set(false);
        this.toast.show(err?.error?.message ?? 'INTEGRATIONS.HUBSPOT.SYNC.ERROR', 'error');
      },
    });
  }

  private clearQueryParam(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { hubspot: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
}
