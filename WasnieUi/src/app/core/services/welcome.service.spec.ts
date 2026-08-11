import { TestBed } from '@angular/core/testing';
import { WelcomeService } from './welcome.service';

const SEEN_KEY = 'wasnie:welcome-seen';

/**
 * The flag, and only the flag. What the modal looks like is not tested here — what matters is that a
 * new user sees it exactly once and that re-watching never rewrites history.
 */
describe('WelcomeService — the "seen it" flag', () => {
  let service: WelcomeService;

  beforeEach(() => {
    localStorage.removeItem(SEEN_KEY);
    TestBed.configureTestingModule({});
    service = TestBed.inject(WelcomeService);
  });

  afterEach(() => localStorage.removeItem(SEEN_KEY));

  it('opens automatically for a browser that has never seen it', () => {
    service.openIfFirstVisit();
    expect(service.isOpen()).toBeTrue();
  });

  it('★ records the flag on close, so a reload does not show it again', () => {
    service.openIfFirstVisit();
    service.close();

    expect(localStorage.getItem(SEEN_KEY)).toBe('1');
    expect(service.isOpen()).toBeFalse();

    // A fresh instance is what the next page load gets.
    const afterReload = new WelcomeService();
    afterReload.openIfFirstVisit();
    expect(afterReload.isOpen())
      .withContext('the tour is a first-run event, not a recurring one').toBeFalse();
  });

  it('does not open automatically when the flag is already set', () => {
    localStorage.setItem(SEEN_KEY, '1');

    service.openIfFirstVisit();

    expect(service.isOpen()).toBeFalse();
  });

  it('★ opening it by hand never writes the flag', () => {
    // Someone who reaches /manual before ever seeing the automatic tour must still get it later.
    service.openManually();
    service.close();

    expect(localStorage.getItem(SEEN_KEY))
      .withContext('re-watching is not the first-run event').toBeNull();

    service.openIfFirstVisit();
    expect(service.isOpen()).withContext('the automatic showing is still owed').toBeTrue();
  });

  it('★ opening it by hand never CLEARS the flag either', () => {
    service.openIfFirstVisit();
    service.close();

    service.openManually();
    service.close();

    expect(localStorage.getItem(SEEN_KEY))
      .withContext('re-watching must not reset the first-run state').toBe('1');
  });

  it('treats unreadable storage as "already seen" rather than looping on every load', () => {
    // Private browsing / storage disabled: throwing here would break the shell's ngOnInit, and
    // guessing "not seen" would reopen the modal on every single navigation.
    spyOn(Storage.prototype, 'getItem').and.throwError('SecurityError');

    expect(service.hasSeen()).toBeTrue();
    service.openIfFirstVisit();
    expect(service.isOpen()).toBeFalse();
  });

  it('does not throw when storage refuses the write', () => {
    spyOn(Storage.prototype, 'setItem').and.throwError('QuotaExceededError');

    service.openIfFirstVisit();

    expect(() => service.close()).not.toThrow();
    expect(service.isOpen())
      .withContext('the modal still closes even if the flag could not be saved').toBeFalse();
  });
});
