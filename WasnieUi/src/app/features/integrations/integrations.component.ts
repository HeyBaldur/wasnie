import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import {
  WsPageLayoutComponent,
  WsCardComponent,
  WsButtonComponent,
  WsBadgeComponent,
  WsInputComponent,
  WsConfirmationModalComponent,
  type BadgeVariant,
} from '../../shared/ui';
import { ToastService } from '../../shared/services/toast.service';
import { DateFormatPipe } from '../../shared/pipes/date-format.pipe';
import { RelativeTimePipe } from '../../shared/pipes/relative-time.pipe';
import { HubSpotApiService } from './services/hubspot.api.service';
import { HubSpotConnectionStatus, HubSpotStatus } from './models/hubspot.model';

/** One numbered step of the connection tutorial. See `tutorialSteps` for what the flags mean. */
interface TutorialStep {
  readonly key: string;
  readonly warning?: boolean;
  readonly scopes?: boolean;
}

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
    WsInputComponent,
    WsConfirmationModalComponent,
    FormsModule,
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

  /**
   * The connection tutorial's steps.
   *
   * ★ A LIST RATHER THAN FOUR HAND-WRITTEN BLOCKS, so the numbering in the markup is derived from the
   * position instead of typed four times — a renumbering done by hand across three languages and one
   * template is a renumbering that ends up disagreeing with itself.
   *
   * Two steps carry more than a title and a sentence, and the flags say which:
   *
   * ★ `warning` (step 2) is the OAuth state's 10-minute TTL (HubSpotOptions.StateTtlMinutes). It earns a
   *   callout because it fails silently and late: the user is on HubSpot's screen by then, and a link
   *   that quietly expired reads as the connector being broken rather than as "press Connect again".
   *
   * ★ `scopes` (step 3) lists the four permissions HubSpot will ask to grant. Naming them is safe HERE
   *   and only here: they are OUR app's, fixed in server config
   *   (HubSpotOptions.Scopes — crm.objects.deals.read, crm.objects.owners.read, crm.schemas.deals.read,
   *   crm.objects.line_items.read), not a marketplace listing that can change under us. A test pins the
   *   count so a fifth scope added on the server cannot leave this page quietly claiming four.
   *
   * The steps are INFORMATION ONLY: nothing here triggers anything. Incentra's whole side of connecting
   * is the Connect button in the card, and every other step happens on HubSpot's own screens.
   */
  readonly tutorialSteps: readonly TutorialStep[] = [
    { key: '1' },
    { key: '2', warning: true },
    { key: '3', scopes: true },
    { key: '4' },
  ];

  /**
   * The four read-only permissions, as key suffixes, in the order HubSpot's own consent screen groups
   * them: the objects first, then the schema, then the line items hanging off a deal.
   */
  readonly tutorialScopes = ['DEALS', 'OWNERS', 'SCHEMAS', 'LINE_ITEMS'] as const;

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly status = signal<HubSpotConnectionStatus | null>(null);

  readonly connecting = signal(false);
  readonly disconnecting = signal(false);
  readonly disconnectConfirmOpen = signal(false);
  readonly testing = signal(false);
  // Inline result of the last "Test connection" run (cleared on reload / disconnect).
  readonly testResult = signal<{ ok: boolean; message: string } | null>(null);

  // Phase 2 deal-sync state. syncResult carries a translation key + optional params (counts).
  readonly previewing = signal(false);
  readonly importing = signal(false);
  readonly syncResult = signal<{ ok: boolean; key: string; params?: Record<string, unknown> } | null>(null);

  // Phase 3 "Sync now" (on-demand trigger of the automatic incremental sync).
  readonly syncingNow = signal(false);

  // WI-CRM-CATEGORY: the HubSpot property name that feeds Category. Bound to the config input;
  // initialised from status on load. Empty = feature off (only the manual lookup table applies).
  categoryPropValue = '';
  readonly savingCategory = signal(false);

  readonly currentStatus = computed<HubSpotStatus>(() => this.status()?.status ?? 'NeverConnected');
  readonly isConnected = computed(() => this.currentStatus() === 'Connected');
  readonly needsReconnect = computed(() => this.currentStatus() === 'NeedsReconnect');
  readonly isDisconnected = computed(() => this.currentStatus() === 'Disconnected');
  readonly neverConnected = computed(() => this.currentStatus() === 'NeverConnected');

  /**
   * The workspace is on Free, so every HubSpot action is refused server-side. Drives the locked card:
   * the actions are withheld and an upgrade path is offered instead of letting the user click into a
   * guaranteed 403. Defaults to false while the status is still loading — the card renders unlocked
   * for an instant rather than accusing a paying tenant of being on Free.
   */
  readonly requiresUpgrade = computed(() => this.status()?.requiresUpgrade === true);

  /** Connected before the downgrade: the connection is intact but frozen until they upgrade. */
  readonly isFrozen = computed(() => this.requiresUpgrade() && (this.isConnected() || this.needsReconnect()));

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
      next: s => {
        this.status.set(s);
        this.categoryPropValue = s.categoryPropertyName ?? '';
        this.loading.set(false);
      },
      error: () => { this.loading.set(false); this.loadError.set(true); },
    });
  }

  /** WI-CRM-CATEGORY: persist the tenant's chosen HubSpot property (empty clears it → feature off). */
  saveCategoryProperty(): void {
    this.savingCategory.set(true);
    const value = this.categoryPropValue.trim();
    this.api.setCategoryProperty(value.length ? value : null).subscribe({
      next: () => {
        this.savingCategory.set(false);
        this.toast.show('INTEGRATIONS.HUBSPOT.CATEGORY.SAVED', 'success');
        this.load();
      },
      error: err => {
        this.savingCategory.set(false);
        this.toast.show(err?.error?.message ?? 'INTEGRATIONS.HUBSPOT.CATEGORY.SAVE_ERROR', 'error');
      },
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

  /// Disconnecting clears the stored OAuth tokens, so it cannot be undone without re-authorizing
  /// in HubSpot — it goes through the app's standard confirmation modal rather than firing on click.
  requestDisconnect(): void {
    this.disconnectConfirmOpen.set(true);
  }

  confirmDisconnect(): void {
    this.disconnecting.set(true);
    this.testResult.set(null);
    this.api.disconnect().subscribe({
      next: () => {
        this.disconnecting.set(false);
        this.disconnectConfirmOpen.set(false);
        this.toast.show('INTEGRATIONS.HUBSPOT.TOAST_DISCONNECTED', 'success');
        this.load();
      },
      error: err => {
        this.disconnecting.set(false);
        // Modal stays open so the error is visible in context and the user can retry or cancel.
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
