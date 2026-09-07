import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { defer, of } from 'rxjs';
import { HubSpotSyncBannerComponent } from './hubspot-sync-banner.component';
import { HubSpotApiService } from '../../services/hubspot.api.service';
import { HubSpotConnectionStatus, HubSpotStatus } from '../../models/hubspot.model';

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
    imports: [HubSpotSyncBannerComponent, TranslateModule.forRoot()],
    providers: [provideRouter([]), { provide: HubSpotApiService, useValue: api }],
  }).compileComponents();
}

async function setup(status: HubSpotConnectionStatus): Promise<ComponentFixture<HubSpotSyncBannerComponent>> {
  await configure({ getStatus: () => of(status) });
  const fixture = TestBed.createComponent(HubSpotSyncBannerComponent);
  fixture.detectChanges();
  return fixture;
}

describe('HubSpotSyncBannerComponent', () => {
  it('renders the banner with a link when HubSpot is Connected', async () => {
    const fixture = await setup(statusOf('Connected', '2026-06-24T09:00:00Z'));
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.hs-sync-banner')).toBeTruthy();
    expect(el.querySelector('a.hs-sync-banner__link')).toBeTruthy();
  });

  it('does NOT render anything when HubSpot is not connected', async () => {
    const fixture = await setup(statusOf('Disconnected', null));
    expect((fixture.nativeElement as HTMLElement).querySelector('.hs-sync-banner')).toBeNull();
  });

  it('does NOT render for NeedsReconnect either (Connected only)', async () => {
    const fixture = await setup(statusOf('NeedsReconnect', '2026-06-24T09:00:00Z'));
    expect((fixture.nativeElement as HTMLElement).querySelector('.hs-sync-banner')).toBeNull();
  });

  /**
   * ★★ THE BLINK, PINNED. Creating the component twice is what a navigation does — the sidebar holding
   * this banner is destroyed and rebuilt on every route change. The second mount must ask nobody and
   * must be complete on its FIRST frame; while the banner fetched for itself, it was absent for one
   * round trip on every click and the aside visibly grew when the answer arrived.
   */
  it('does not ask again when the sidebar is rebuilt', async () => {
    const api = countingApi(statusOf('Connected', '2026-06-24T09:00:00Z'));
    await configure(api);

    const first = TestBed.createComponent(HubSpotSyncBannerComponent);
    first.detectChanges();
    first.destroy();

    const second = TestBed.createComponent(HubSpotSyncBannerComponent);
    second.detectChanges();

    expect(api.calls).toBe(1, 'a request per navigation is the defect this replaced');
    expect((second.nativeElement as HTMLElement).querySelector('.hs-sync-banner'))
      .withContext('the rebuilt banner must be there on the first frame, not one round trip later')
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

    const first = TestBed.createComponent(HubSpotSyncBannerComponent);
    first.detectChanges();
    expect((first.nativeElement as HTMLElement).querySelector('.hs-sync-banner'))
      .withContext('first load of the session: nothing to show yet')
      .toBeNull();

    resolve(statusOf('Connected', '2026-06-24T09:00:00Z'));
    await pending;
    first.detectChanges();
    expect((first.nativeElement as HTMLElement).querySelector('.hs-sync-banner')).toBeTruthy();

    first.destroy();
    const second = TestBed.createComponent(HubSpotSyncBannerComponent);
    second.detectChanges();

    expect((second.nativeElement as HTMLElement).querySelector('.hs-sync-banner'))
      .withContext('every rebuild after that is instant — this is what removes the jump')
      .toBeTruthy();
  });
});
