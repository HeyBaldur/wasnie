import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { RelativeTimePipe } from '../../../../shared/pipes/relative-time.pipe';
import { WsCardComponent } from '../../../../shared/ui';
import { HubSpotApiService } from '../../services/hubspot.api.service';
import { HubSpotConnectionStatus } from '../../models/hubspot.model';

/**
 * Discreet, READ-ONLY reminder (shown above the Transactions filters) that HubSpot deals sync
 * automatically, with the live "last synced X ago". Renders ONLY when HubSpot is Connected for the
 * tenant — nothing for anyone who doesn't use HubSpot. By design it has NO sync action (anti-spam):
 * manual sync lives on the Integrations page, which this banner links to. Reuses the existing HubSpot
 * status endpoint and the shared relativeTime pipe — no new backend.
 */
@Component({
  selector: 'app-hubspot-sync-banner',
  standalone: true,
  imports: [RouterLink, TranslateModule, RelativeTimePipe, WsCardComponent],
  templateUrl: './hubspot-sync-banner.component.html',
  styleUrl: './hubspot-sync-banner.component.scss',
})
export class HubSpotSyncBannerComponent implements OnInit {
  private readonly api = inject(HubSpotApiService);

  readonly status = signal<HubSpotConnectionStatus | null>(null);
  readonly connected = computed(() => this.status()?.status === 'Connected');

  ngOnInit(): void {
    this.api.getStatus().subscribe({
      next: (s) => this.status.set(s),
      error: () => this.status.set(null), // silent — if status can't be read, just show nothing
    });
  }
}
