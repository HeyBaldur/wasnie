import { TestBed } from '@angular/core/testing';
import { TabSyncService, TabSyncMessage } from './tab-sync.service';

// ─── Helpers ──────────────────────────────────────────────────────────────────

/** Creates a minimal BroadcastChannel constructor mock that captures the instance. */
function makeBroadcastChannelMock(): {
  instance: { postMessage: jasmine.Spy; onmessage: ((e: MessageEvent) => void) | null; close: jasmine.Spy };
  ctor: jasmine.Spy;
} {
  const instance = { postMessage: jasmine.createSpy('postMessage'), onmessage: null as any, close: jasmine.createSpy('close') };
  const ctor = jasmine.createSpy('BroadcastChannel').and.returnValue(instance);
  return { instance, ctor };
}

// ─── TabSyncService — BroadcastChannel path ───────────────────────────────────
describe('TabSyncService — BroadcastChannel path', () => {
  let service: TabSyncService;
  let mock: ReturnType<typeof makeBroadcastChannelMock>;

  beforeEach(() => {
    mock = makeBroadcastChannelMock();
    (window as any).BroadcastChannel = mock.ctor;

    TestBed.configureTestingModule({});
    service = TestBed.inject(TabSyncService);
  });

  afterEach(() => {
    service.ngOnDestroy();
    delete (window as any).BroadcastChannel;
  });

  it('opens a channel named "wasnie-session"', () => {
    expect(mock.ctor).toHaveBeenCalledWith('wasnie-session');
  });

  it('broadcast() calls postMessage with the message object', () => {
    service.broadcast({ type: 'logout' });
    expect(mock.instance.postMessage).toHaveBeenCalledOnceWith({ type: 'logout' });
  });

  it('broadcasts each signal type without including auth tokens', () => {
    const types: TabSyncMessage['type'][] = ['activity', 'logout', 'session-expired'];
    types.forEach(type => {
      service.broadcast({ type });
      const call = mock.instance.postMessage.calls.mostRecent();
      expect(call.args[0]).toEqual({ type });
      expect(Object.keys(call.args[0])).toEqual(['type']); // no extra fields (no token)
    });
  });

  it('emits through messages$ when onmessage fires', () => {
    const received: TabSyncMessage[] = [];
    service.messages$.subscribe(m => received.push(m));

    mock.instance.onmessage!({ data: { type: 'activity' } } as MessageEvent);
    mock.instance.onmessage!({ data: { type: 'session-expired' } } as MessageEvent);

    expect(received).toEqual([{ type: 'activity' }, { type: 'session-expired' }]);
  });

  it('ngOnDestroy closes the channel', () => {
    service.ngOnDestroy();
    expect(mock.instance.close).toHaveBeenCalled();
  });

  it('ngOnDestroy completes messages$', () => {
    let completed = false;
    service.messages$.subscribe({ complete: () => (completed = true) });
    service.ngOnDestroy();
    expect(completed).toBeTrue();
  });
});

// ─── TabSyncService — localStorage fallback path ──────────────────────────────
describe('TabSyncService — localStorage fallback path', () => {
  let service: TabSyncService;
  let originalBC: typeof BroadcastChannel | undefined;

  beforeEach(() => {
    // Ensure BroadcastChannel is undefined so the service uses the fallback
    originalBC = (window as any).BroadcastChannel;
    delete (window as any).BroadcastChannel;

    spyOn(localStorage, 'setItem');

    TestBed.configureTestingModule({});
    service = TestBed.inject(TabSyncService);
  });

  afterEach(() => {
    service.ngOnDestroy();
    if (originalBC !== undefined) {
      (window as any).BroadcastChannel = originalBC;
    }
  });

  it('broadcast() writes a timestamped entry to localStorage', () => {
    service.broadcast({ type: 'logout' });
    expect(localStorage.setItem).toHaveBeenCalledOnceWith(
      'wasnie:tab-sync',
      jasmine.stringMatching(/"type":"logout"/)
    );
  });

  it('emits through messages$ when a storage event arrives for the sync key', () => {
    const received: TabSyncMessage[] = [];
    service.messages$.subscribe(m => received.push(m));

    window.dispatchEvent(new StorageEvent('storage', {
      key: 'wasnie:tab-sync',
      newValue: JSON.stringify({ type: 'session-expired', _t: Date.now() }),
    }));

    expect(received).toEqual([{ type: 'session-expired' }]);
  });

  it('ignores storage events for unrelated keys', () => {
    const received: TabSyncMessage[] = [];
    service.messages$.subscribe(m => received.push(m));

    window.dispatchEvent(new StorageEvent('storage', {
      key: 'wasnie_session',
      newValue: '{"accessToken":"tok"}',
    }));

    expect(received).toEqual([]);
  });

  it('ignores storage events with unknown type values', () => {
    const received: TabSyncMessage[] = [];
    service.messages$.subscribe(m => received.push(m));

    window.dispatchEvent(new StorageEvent('storage', {
      key: 'wasnie:tab-sync',
      newValue: JSON.stringify({ type: 'unknown-signal', _t: Date.now() }),
    }));

    expect(received).toEqual([]);
  });

  it('ignores malformed JSON in storage events', () => {
    const received: TabSyncMessage[] = [];
    service.messages$.subscribe(m => received.push(m));

    window.dispatchEvent(new StorageEvent('storage', {
      key: 'wasnie:tab-sync',
      newValue: 'NOT_JSON',
    }));

    expect(received).toEqual([]);
  });
});
