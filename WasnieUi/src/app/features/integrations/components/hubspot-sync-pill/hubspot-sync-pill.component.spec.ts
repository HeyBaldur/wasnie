import { signal } from '@angular/core';
import { AuthService } from '../../../../core/services/auth.service';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { TranslateModule } from '@ngx-translate/core';
import { defer, of } from 'rxjs';
import { HubSpotSyncPillComponent } from './hubspot-sync-pill.component';
import { HubSpotApiService } from '../../services/hubspot.api.service';
import { HubSpotConnectionStatus, HubSpotStatus } from '../../models/hubspot.model';
import { WsTooltipDirective } from '../../../../shared/ui';

function statusOf(status: HubSpotStatus, lastSyncedAt: string | null): HubSpotConnectionStatus {
  return {
    status,
    portalId: 1,
    statusReason: null,
    connectedAt: '2026-06-20T09:00:00Z',
    connectedBy: 'owner',
    disconnectedAt: null,
    lastSyncedAt,
    categoryPropertyName: null,
    requiresUpgrade: false,
  };
}

/** Counts the calls, so a test can say "and it did NOT ask again". */
function countingApi(status: HubSpotConnectionStatus): { calls: number; getStatus: () => unknown } {
  const api = {
    calls: 0,
    getStatus: () => {
      api.calls++;
      return of(status);
    },
  };
  return api;
}

async function configure(api: unknown): Promise<void> {
  await TestBed.configureTestingModule({
    imports: [HubSpotSyncPillComponent, TranslateModule.forRoot()],
    providers: [
      provideRouter([]),
      { provide: HubSpotApiService, useValue: api },
      { provide: AuthService, useValue: { tenantId: signal('tenant-a') } },
    ],
  }).compileComponents();
}

async function setup(status: HubSpotConnectionStatus): Promise<ComponentFixture<HubSpotSyncPillComponent>> {
  await configure({ getStatus: () => of(status) });
  const fixture = TestBed.createComponent(HubSpotSyncPillComponent);
  fixture.detectChanges();
  return fixture;
}

describe('HubSpotSyncPillComponent', () => {
  it('renders the pill when HubSpot is Connected', async () => {
    const fixture = await setup(statusOf('Connected', '2026-06-24T09:00:00Z'));
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.hs-sync-pill')).toBeTruthy();
  });

  /**
   * ★★ THE PILL IS THE LINK ITSELF. The card it replaces carried a separate "Go to integration page →"
   * anchor; a pill has no room for one, so the whole pill has to be the anchor — otherwise moving this
   * into the topbar would have quietly removed the only way to reach the Integrations page from here.
   */
  it('★★ the whole pill is an anchor to the integrations page', async () => {
    const fixture = await setup(statusOf('Connected', '2026-06-24T09:00:00Z'));
    const pill = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('a.hs-sync-pill');

    expect(pill).toBeTruthy();
    expect(pill!.getAttribute('href')).toBe('/integrations');
  });

  /**
   * ★ THE SENTENCE SURVIVED THE SHRINK, IN THE TOOLTIP. "Sync runs every hour" is the reason the pill
   * means anything, and it is the one thing a pill is too small to say. Losing it on the way from the
   * card to the topbar would have left a bare timestamp nobody can interpret.
   */
  it('★ keeps the full explanation as the tooltip', async () => {
    const fixture = await setup(statusOf('Connected', '2026-06-24T09:00:00Z'));
    // The rendered DOM cannot answer this — the tooltip is built on hover and appended to <body>. The
    // binding is the thing under test, so it is read off the directive.
    const tip = fixture.debugElement.query(By.directive(WsTooltipDirective))
      .injector.get(WsTooltipDirective);

    // TranslateModule.forRoot() with no translations echoes the key back, which is what identifies it.
    expect(tip.wsTooltip()).toBe('TRANSACTIONS.HS_SYNC_BANNER');
  });

  it('★ and says "not synced yet" rather than an empty hover when it never ran', async () => {
    const fixture = await setup(statusOf('Connected', null));
    const tip = fixture.debugElement.query(By.directive(WsTooltipDirective))
      .injector.get(WsTooltipDirective);

    expect(tip.wsTooltip()).toBe('TRANSACTIONS.HS_SYNC_BANNER_NEVER');
  });

  it('does NOT render anything when HubSpot is not connected', async () => {
    const fixture = await setup(statusOf('Disconnected', null));
    expect((fixture.nativeElement as HTMLElement).querySelector('.hs-sync-pill')).toBeNull();
  });

  it('does NOT render for NeedsReconnect either (Connected only)', async () => {
    const fixture = await setup(statusOf('NeedsReconnect', '2026-06-24T09:00:00Z'));
    expect((fixture.nativeElement as HTMLElement).querySelector('.hs-sync-pill')).toBeNull();
  });

  /**
   * ★★ THE BLINK, PINNED. Creating the component twice is what a navigation does — the topbar holding
   * this pill, like the sidebar that held the card before it, is destroyed and rebuilt on every route
   * change. The second mount must ask nobody and must be complete on its FIRST frame; while it fetched
   * for itself, it was absent for one round trip on every click and the row of controls visibly shifted.
   */
  it('does not ask again when the topbar is rebuilt', async () => {
    const api = countingApi(statusOf('Connected', '2026-06-24T09:00:00Z'));
    await configure(api);

    const first = TestBed.createComponent(HubSpotSyncPillComponent);
    first.detectChanges();
    first.destroy();

    const second = TestBed.createComponent(HubSpotSyncPillComponent);
    second.detectChanges();

    expect(api.calls).toBe(1, 'a request per navigation is the defect this replaced');
    expect((second.nativeElement as HTMLElement).querySelector('.hs-sync-pill'))
      .withContext('the rebuilt pill must be there on the first frame, not one round trip later')
      .toBeTruthy();
  });

  /**
   * ★ The gap only ever happens once. The very first load of the session genuinely has to wait for the
   * response — that is unavoidable and is not what the user sees. What the user saw was that same gap
   * repeating on every single click, and after the first load there is no gap left to repeat.
   */
  it('is complete on the first frame of a rebuild, gap spent', async () => {
    let resolve!: (s: HubSpotConnectionStatus) => void;
    const pending = new Promise<HubSpotConnectionStatus>(r => (resolve = r));
    await configure({ getStatus: () => defer(() => pending) });

    const first = TestBed.createComponent(HubSpotSyncPillComponent);
    first.detectChanges();
    expect((first.nativeElement as HTMLElement).querySelector('.hs-sync-pill'))
      .withContext('first load of the session: nothing to show yet')
      .toBeNull();

    resolve(statusOf('Connected', '2026-06-24T09:00:00Z'));
    await pending;
    first.detectChanges();
    expect((first.nativeElement as HTMLElement).querySelector('.hs-sync-pill')).toBeTruthy();

    first.destroy();
    const second = TestBed.createComponent(HubSpotSyncPillComponent);
    second.detectChanges();

    expect((second.nativeElement as HTMLElement).querySelector('.hs-sync-pill'))
      .withContext('every rebuild after that is instant — this is what removes the jump')
      .toBeTruthy();
  });
});
