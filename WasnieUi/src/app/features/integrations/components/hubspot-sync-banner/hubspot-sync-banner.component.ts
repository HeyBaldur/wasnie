import { Component, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { RelativeTimePipe } from '../../../../shared/pipes/relative-time.pipe';
import { WsCardComponent } from '../../../../shared/ui';
import { HubSpotStatusStore } from '../../services/hubspot-status.store';

/**
 * Discreet, READ-ONLY reminder (shown in the sidebar) that HubSpot deals sync automatically, with the
 * live "last synced X ago". Renders ONLY when HubSpot is Connected for the tenant — nothing for anyone
 * who doesn't use HubSpot. By design it has NO sync action (anti-spam): manual sync lives on the
 * Integrations page, which this banner links to.
 *
 * ★★ IT OWNS NO STATE AND MAKES NO REQUEST OF ITS OWN. It sits in the sidebar, which is rebuilt on
 * every navigation, so a fetch here was a request per click AND a visible jump: the banner was absent
 * for one round trip and the aside grew when it arrived. The status is cached session-wide in
 * {@link HubSpotStatusStore}, so on every rebuild after the first this renders on the FIRST frame.
 */
@Component({
  selector: 'app-hubspot-sync-banner',
  standalone: true,
  imports: [RouterLink, TranslateModule, RelativeTimePipe, WsCardComponent],
  templateUrl: './hubspot-sync-banner.component.html',
  styleUrl: './hubspot-sync-banner.component.scss',
})
export class HubSpotSyncBannerComponent implements OnInit {
  private readonly store = inject(HubSpotStatusStore);

  readonly status = this.store.status;
  readonly connected = this.store.connected;

  ngOnInit(): void {
    this.store.ensureLoaded();
  }
}
