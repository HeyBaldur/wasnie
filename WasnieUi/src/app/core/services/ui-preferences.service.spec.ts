import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { UI_PREFERENCE_KEYS, UiPreferencesService, isTwoFaReminderSuppressed } from './ui-preferences.service';
import { environment } from '../../../environments/environment';

describe('UiPreferencesService (KAN-78)', () => {
  const URL = `${environment.apiBaseUrl}/me/ui-preferences`;
  let user: WritableSignal<{ userId: string } | null>;
  let service: UiPreferencesService;
  let http: HttpTestingController;

  beforeEach(() => {
    ['wasnie:welcome-seen', 'wasnie:sandbox-intro-dismissed', 'wasnie:2fa-reminder-dismissed', 'wasnie:2fa-reminder-snooze']
      .forEach(k => localStorage.removeItem(k));
    user = signal<{ userId: string } | null>({ userId: 'u-1' });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { currentUser: user, isAuthenticated: () => user() !== null } },
      ],
    });
    service = TestBed.inject(UiPreferencesService);
    http = TestBed.inject(HttpTestingController);
    TestBed.flushEffects();
  });

  afterEach(() => http.verify());

  it('loads the user\'s preferences once and answers from them', async () => {
    const first = service.ensureLoaded();
    const second = service.ensureLoaded();
    http.expectOne(URL).flush({ 'welcome-seen': 'true' });

    expect(await first).toBeTrue();
    expect(await second).toBeTrue();
    expect(service.isFlagSet(UI_PREFERENCE_KEYS.welcomeSeen)).toBeTrue();
    expect(service.isFlagSet(UI_PREFERENCE_KEYS.sandboxIntroDismissed)).toBeFalse();
  });

  it('★ a failed load resolves false — callers must not treat unknown as "not seen"', async () => {
    const loaded = service.ensureLoaded();
    http.expectOne(URL).flush(null, { status: 500, statusText: 'boom' });
    expect(await loaded).toBeFalse();
    expect(service.loaded()).toBeFalse();
  });

  it('★ a choice is applied at once and saved on the server under its key', async () => {
    const loaded = service.ensureLoaded();
    http.expectOne(URL).flush({});
    await loaded;

    const saved = service.setFlag(UI_PREFERENCE_KEYS.welcomeSeen);
    expect(service.isFlagSet(UI_PREFERENCE_KEYS.welcomeSeen)).toBeTrue();
    const put = http.expectOne(`${URL}/welcome-seen`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ value: 'true' });
    put.flush(null);
    await saved;
  });

  it('★ switching users forgets the previous user\'s answers', async () => {
    const loaded = service.ensureLoaded();
    http.expectOne(URL).flush({ 'welcome-seen': 'true' });
    await loaded;

    user.set({ userId: 'u-2' });
    TestBed.flushEffects();

    expect(service.loaded()).toBeFalse();
    expect(service.isFlagSet(UI_PREFERENCE_KEYS.welcomeSeen)).toBeFalse();
    const reloaded = service.ensureLoaded();
    http.expectOne(URL).flush({});
    await reloaded;
    expect(service.isFlagSet(UI_PREFERENCE_KEYS.welcomeSeen)).toBeFalse();
  });

  it('adopts an old browser flag once, so shipping this does not replay the welcome', async () => {
    localStorage.setItem('wasnie:welcome-seen', '1');

    const loaded = service.ensureLoaded();
    http.expectOne(URL).flush({});
    await loaded;

    http.expectOne(`${URL}/welcome-seen`).flush(null);
    expect(service.isFlagSet(UI_PREFERENCE_KEYS.welcomeSeen)).toBeTrue();
    expect(localStorage.getItem('wasnie:welcome-seen')).toBeNull();
  });

  it('never overwrites a server value with an old browser flag', async () => {
    localStorage.setItem('wasnie:2fa-reminder-dismissed', 'true');

    const loaded = service.ensureLoaded();
    http.expectOne(URL).flush({ 'twofa-reminder': '{"state":"snoozed","until":"2099-01-01T00:00:00Z"}' });
    await loaded;

    http.expectNone(`${URL}/twofa-reminder`);
  });

  describe('isTwoFaReminderSuppressed', () => {
    const now = new Date('2026-09-14T10:00:00Z');

    it('"Don\'t show again" suppresses it forever', () => {
      expect(isTwoFaReminderSuppressed('{"state":"dismissed"}', now)).toBeTrue();
    });

    it('★ "Remind me in 3 days" suppresses it only until the stored date', () => {
      expect(isTwoFaReminderSuppressed('{"state":"snoozed","until":"2026-09-17T10:00:00Z"}', now)).toBeTrue();
      expect(isTwoFaReminderSuppressed('{"state":"snoozed","until":"2026-09-14T09:59:59Z"}', now)).toBeFalse();
    });

    it('no choice, or an unreadable one, suppresses nothing', () => {
      expect(isTwoFaReminderSuppressed(null, now)).toBeFalse();
      expect(isTwoFaReminderSuppressed('not json', now)).toBeFalse();
      expect(isTwoFaReminderSuppressed('{"state":"snoozed"}', now)).toBeFalse();
    });
  });
});
