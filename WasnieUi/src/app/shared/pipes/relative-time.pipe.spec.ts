import { TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';
import { RelativeTimePipe } from './relative-time.pipe';

describe('RelativeTimePipe (stable within a change detection cycle — NG0100)', () => {
  let pipe: RelativeTimePipe;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TranslateModule.forRoot()],
      providers: [RelativeTimePipe],
    });
    pipe = TestBed.inject(RelativeTimePipe);
  });

  it('returns an empty string for a missing value', () => {
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
    expect(pipe.transform('')).toBe('');
  });

  it('formats a recent instant in seconds', () => {
    const fifteenSecondsAgo = new Date(Date.now() - 15_000).toISOString();
    expect(pipe.transform(fifteenSecondsAgo)).toContain('15');
  });

  it('scales the unit with the distance', () => {
    expect(pipe.transform(new Date(Date.now() - 5 * 60_000).toISOString())).toContain('5');
    expect(pipe.transform(new Date(Date.now() - 3 * 3_600_000).toISOString())).toContain('3');
    expect(pipe.transform(new Date(Date.now() - 4 * 86_400_000).toISOString())).toContain('4');
  });

  it('gives the SAME string when re-evaluated a second later — the dev-mode verification pass must not see the value move', () => {
    const value = new Date(Date.now() - 15_000).toISOString();
    const first = pipe.transform(value);

    // Simulate the clock crossing a second between the two passes of one cycle.
    jasmine.clock().install();
    try {
      jasmine.clock().mockDate(new Date(Date.now() + 900));
      expect(pipe.transform(value)).toBe(first);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('recomputes once the refresh window has passed', () => {
    const value = new Date(Date.now() - 15_000).toISOString();
    const first = pipe.transform(value);

    jasmine.clock().install();
    try {
      jasmine.clock().mockDate(new Date(Date.now() + 5_000));
      expect(pipe.transform(value)).not.toBe(first);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('does not serve a cached string to a different value', () => {
    const recent = new Date(Date.now() - 15_000).toISOString();
    const old = new Date(Date.now() - 4 * 86_400_000).toISOString();
    const first = pipe.transform(recent);
    expect(pipe.transform(old)).not.toBe(first);
  });
});
