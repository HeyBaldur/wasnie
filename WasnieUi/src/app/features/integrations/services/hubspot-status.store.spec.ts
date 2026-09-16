import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of, Subject, throwError } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { HubSpotStatusStore } from './hubspot-status.store';
import { HubSpotApiService } from './hubspot.api.service';
import { HubSpotConnectionStatus, HubSpotStatus } from '../models/hubspot.model';

function statusOf(status: HubSpotStatus): HubSpotConnectionStatus {
  return {
    status,
    portalId: 1,
    statusReason: null,
    connectedAt: '2026-06-20T09:00:00Z',
    connectedBy: 'owner',
    disconnectedAt: null,
    lastSyncedAt: '2026-06-24T09:00:00Z',
    categoryPropertyName: null,
    requiresUpgrade: false,
  };
}

describe('HubSpotStatusStore', () => {
  let calls: number;
  let tenantId: WritableSignal<string | null>;

  function configure(getStatus: () => Observable<HubSpotConnectionStatus>): HubSpotStatusStore {
    calls = 0;
    tenantId = signal<string | null>('tenant-a');
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { tenantId } },
        {
        provide: HubSpotApiService,
        useValue: {
          getStatus: () => {
            calls++;
            return getStatus();
          },
        },
      }],
    });
    return TestBed.inject(HubSpotStatusStore);
  }

  it('reads the status once and answers from memory after that', () => {
    const store = configure(() => of(statusOf('Connected')));

    store.ensureLoaded();
    store.ensureLoaded();
    store.ensureLoaded();

    expect(calls).toBe(1, 'ensureLoaded runs on every navigation; only the first may fetch');
    expect(store.connected()).toBe(true);
  });

  it('shows nothing rather than an error when the status cannot be read', () => {
    const store = configure(() => throwError(() => new Error('boom')));

    store.ensureLoaded();

    expect(store.status()).toBeNull();
    expect(store.connected()).toBe(false);
  });

  /**
   * ★★ THE STALENESS THIS CACHE COULD HAVE CAUSED. Disconnecting HubSpot must take the banner away
   * immediately — a cached "Connected" that outlives the connection would tell the user their deals
   * are still syncing when nothing is. The Integrations page is the only place this changes, and it
   * pushes every status it fetches.
   */
  it('takes a fresh status from the Integrations page, without re-fetching', () => {
    const store = configure(() => of(statusOf('Connected')));
    store.ensureLoaded();
    expect(store.connected()).toBe(true);

    store.set(statusOf('Disconnected'));

    expect(store.connected()).toBe(false, 'the banner must go when the connection goes');
    expect(calls).toBe(1);
  });

  /** ★ A status pushed before anything asked counts as the load — the page already did the work. */
  it('treats a pushed status as the load', () => {
    const store = configure(() => of(statusOf('Connected')));

    store.set(statusOf('Disconnected'));
    store.ensureLoaded();

    expect(calls).toBe(0);
    expect(store.connected()).toBe(false);
  });
  /**
   * ★★ KAN-77 runtime: signed in as another account in the same tab, the previous tenant's "Connected"
   * banner stayed — for a tenant that had never connected HubSpot.
   */
  it('★ switching tenant never shows the previous tenant\'s status, and reads the new tenant\'s own', () => {
    const responses: HubSpotConnectionStatus[] = [statusOf('Connected'), statusOf('Disconnected')];
    const store = configure(() => of(responses.shift()!));

    store.ensureLoaded();
    expect(store.connected()).toBe(true);

    tenantId.set('tenant-b');
    expect(store.connected()).toBe(false, 'tenant A\'s cached status must not be shown to tenant B');

    store.ensureLoaded();
    expect(calls).toBe(2, 'the new tenant gets its own read');
    expect(store.connected()).toBe(false);
  });

  it('★ a response that lands after the tenant changed is dropped', () => {
    const pending = new Subject<HubSpotConnectionStatus>();
    const store = configure(() => pending.asObservable());

    store.ensureLoaded();
    tenantId.set('tenant-b');
    pending.next(statusOf('Connected'));

    expect(store.status()).toBeNull();
    expect(store.connected()).toBe(false);
  });
});
