import { TestBed } from '@angular/core/testing';
import { WsToastService } from './ws-toast.service';

/**
 * The countdown behind a toast.
 *
 * ★★ THE CLOCK IS MOCKED, INCLUDING `Date`. `pause` measures elapsed time with `Date.now()`, so a
 * test that only faked `setTimeout` would compute an elapsed of zero and pass no matter what the
 * subtraction did — green, and blind to the one line worth testing. `mockDate` makes `tick` move
 * both.
 */
describe('WsToastService — the countdown', () => {
  let service: WsToastService;

  beforeEach(() => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(2026, 0, 1));
    TestBed.configureTestingModule({});
    service = TestBed.inject(WsToastService);
    service.toasts.set([]);
  });

  afterEach(() => jasmine.clock().uninstall());

  function showOne(): string {
    service.show('COMMON.DISMISS', 'success');
    return service.toasts()[service.toasts().length - 1].id;
  }

  it('dismisses itself after four seconds when nobody is reading', () => {
    showOne();
    expect(service.toasts().length).toBe(1);

    jasmine.clock().tick(3999);
    expect(service.toasts().length).toBe(1);

    jasmine.clock().tick(1);
    expect(service.toasts().length).toBe(0);
  });

  /** ★ The whole request: a toast being read does not vanish, however long it is read for. */
  it('stays up for as long as the reader is on it', () => {
    const id = showOne();

    jasmine.clock().tick(2000);
    service.pause(id);

    jasmine.clock().tick(60_000);
    expect(service.toasts().length).toBe(1);
  });

  /**
   * ★★ THE ASSERTION THAT SEPARATES THIS FROM THE EASY VERSION. Resuming must carry on with the
   * 2000ms that were left, not start a fresh 4000. Restarting would look identical in a hover test
   * and be wrong: a long message the reader keeps glancing at would reset itself for ever.
   */
  it('resumes with the time it had left, not with a fresh four seconds', () => {
    const id = showOne();

    jasmine.clock().tick(2000);
    service.pause(id);
    jasmine.clock().tick(10_000);
    service.resume(id);

    jasmine.clock().tick(1999);
    expect(service.toasts().length).toBe(1);

    jasmine.clock().tick(1);
    expect(service.toasts().length)
      .withContext('2000ms had elapsed before the pause, so 2000ms remained after it')
      .toBe(0);
  });

  /**
   * ★ Browsers re-fire `mouseenter` when the pointer crosses a child in some layouts. A second
   * pause must not subtract the elapsed time twice — that would leave the toast with less time than
   * the reader is owed, or none at all.
   */
  it('does not lose time when paused twice', () => {
    const id = showOne();

    jasmine.clock().tick(3000);
    service.pause(id);
    jasmine.clock().tick(5000);
    service.pause(id);
    service.resume(id);

    jasmine.clock().tick(999);
    expect(service.toasts().length).toBe(1);

    jasmine.clock().tick(1);
    expect(service.toasts().length).toBe(0);
  });

  it('ignores resume on a toast that is already running', () => {
    const id = showOne();

    jasmine.clock().tick(1000);
    service.resume(id);

    jasmine.clock().tick(2999);
    expect(service.toasts().length)
      .withContext('a stray resume must not restart the clock')
      .toBe(1);

    jasmine.clock().tick(1);
    expect(service.toasts().length).toBe(0);
  });

  /** ★ Each toast owns its own countdown: pausing one must not hold the others up. */
  it('pauses one toast without touching the rest', () => {
    const first = showOne();
    showOne();

    jasmine.clock().tick(1000);
    service.pause(first);

    jasmine.clock().tick(3000);
    expect(service.toasts().length).toBe(1);
    expect(service.toasts()[0].id).toBe(first);
  });

  /** ★ Dismissing by hand clears the pending timer; nothing is left to fire at a dead id. */
  it('leaves no timer behind when dismissed by hand', () => {
    const id = showOne();
    service.dismiss(id);

    expect(service.toasts().length).toBe(0);
    expect(() => jasmine.clock().tick(10_000)).not.toThrow();
    expect(service.toasts().length).toBe(0);
  });

  it('does nothing when paused or resumed with an unknown id', () => {
    expect(() => service.pause('nope')).not.toThrow();
    expect(() => service.resume('nope')).not.toThrow();
  });
});
