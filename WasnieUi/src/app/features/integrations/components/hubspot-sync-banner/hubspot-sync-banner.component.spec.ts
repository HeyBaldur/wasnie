import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
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

async function setup(status: HubSpotConnectionStatus): Promise<ComponentFixture<HubSpotSyncBannerComponent>> {
  const api = { getStatus: () => of(status) } as Partial<HubSpotApiService>;
  await TestBed.configureTestingModule({
    imports: [HubSpotSyncBannerComponent, TranslateModule.forRoot()],
    providers: [provideRouter([]), { provide: HubSpotApiService, useValue: api }],
  }).compileComponents();
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
});
