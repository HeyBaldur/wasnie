import { TestBed } from '@angular/core/testing';
import { WelcomeService } from './welcome.service';
import { UI_PREFERENCE_KEYS, UiPreferencesService } from './ui-preferences.service';

/**
 * The flag, and only the flag. What the modal looks like is not tested here — what matters is that a
 * new USER sees it exactly once and that re-watching never rewrites history (KAN-78: the flag lives in
 * the user's preferences on the server, not in the browser).
 */
describe('WelcomeService — the "seen it" flag', () => {
  let service: WelcomeService;
  let values: Record<string, string>;
  let loads: boolean;
  let setFlag: jasmine.Spy;

  beforeEach(() => {
    values = {};
    loads = true;
    setFlag = jasmine.createSpy('setFlag').and.callFake((key: string) => {
      values[key] = 'true';
      return Promise.resolve();
    });
    TestBed.configureTestingModule({
      providers: [
        {
          provide: UiPreferencesService,
          useValue: {
            ensureLoaded: () => Promise.resolve(loads),
            isFlagSet: (key: string) => values[key] === 'true',
            setFlag,
          },
        },
      ],
    });
    service = TestBed.inject(WelcomeService);
  });

  it('opens automatically for a user who has never seen it', async () => {
    await service.openIfFirstVisit();
    expect(service.isOpen()).toBeTrue();
  });

  it('★ records the flag on close, so it does not show again — for this user, anywhere', async () => {
    await service.openIfFirstVisit();
    service.close();

    expect(setFlag).toHaveBeenCalledWith(UI_PREFERENCE_KEYS.welcomeSeen);
    expect(service.isOpen()).toBeFalse();

    await service.openIfFirstVisit();
    expect(service.isOpen()).withContext('the tour is a first-run event, not a recurring one').toBeFalse();
  });

  it('does not open automatically when the user already saw it (another machine, another session)', async () => {
    values[UI_PREFERENCE_KEYS.welcomeSeen] = 'true';

    await service.openIfFirstVisit();

    expect(service.isOpen()).toBeFalse();
  });

  it('★ opening it by hand never writes the flag', async () => {
    service.openManually();
    service.close();

    expect(setFlag).not.toHaveBeenCalled();

    await service.openIfFirstVisit();
    expect(service.isOpen()).withContext('the automatic showing is still owed').toBeTrue();
  });

  it('★ preferences that cannot be loaded count as "seen" — never a welcome on a guess', async () => {
    loads = false;

    await service.openIfFirstVisit();

    expect(service.isOpen()).toBeFalse();
  });
});
