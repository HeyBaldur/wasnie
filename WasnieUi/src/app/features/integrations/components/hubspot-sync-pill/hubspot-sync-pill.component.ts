import { Component, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { RelativeTimePipe } from '../../../../shared/pipes/relative-time.pipe';
import { WsTooltipDirective } from '../../../../shared/ui';
import { HubSpotStatusStore } from '../../services/hubspot-status.store';

/**
 * HubSpot's automatic sync, as a TOPBAR STATUS — the same shape as the trial pill beside it.
 *
 * ★★ IT WAS A CARD IN THE SIDEBAR AND THAT WAS THE WRONG WEIGHT. A logo, a rule, a two-line sentence and a
 * link, permanently parked above the SETTINGS block, gave a background job that changes once an hour more
 * room than any navigation item — and it was the only card in the aside. The fact it carries is one line
 * long ("the sync is alive, it last ran X ago"), so it is now one line: a pill that states it, with the
 * full sentence one hover away and the Integrations page one click away.
 *
 * ★ STILL READ-ONLY, STILL NO SYNC ACTION (anti-spam). Manual sync lives on the Integrations page, which
 * this pill links to — that was true of the card and is not being changed here, only its size.
 *
 * ★★ IT OWNS NO STATE AND MAKES NO REQUEST OF ITS OWN. The topbar, like the sidebar before it, is rebuilt
 * on every navigation (each feature template renders its own `<app-shell>`), so a fetch here would be a
 * request per click AND a visible flicker. The status is cached per tenant for the session in
 * {@link HubSpotStatusStore}, so every rebuild after the first renders on the FIRST frame.
 */
@Component({
  selector: 'app-hubspot-sync-pill',
  standalone: true,
  imports: [RouterLink, TranslateModule, RelativeTimePipe, WsTooltipDirective],
  templateUrl: './hubspot-sync-pill.component.html',
  styleUrl: './hubspot-sync-pill.component.scss',
})
export class HubSpotSyncPillComponent implements OnInit {
  private readonly store = inject(HubSpotStatusStore);

  readonly status = this.store.status;
  readonly connected = this.store.connected;

  ngOnInit(): void {
    this.store.ensureLoaded();
  }
}
