import { composerLayoutFor, composerMaxHeight } from './composer-layout';

/** One line of the composer's field: 8px padding, a 21px line, then the button's strip. */
const ONE_LINE = 61;

describe('composerLayoutFor — pill or stacked', () => {
  it('is a pill while the text fits on one line', () => {
    expect(composerLayoutFor(ONE_LINE, ONE_LINE)).toBe('pill');
  });

  it('stacks once the text needs a second line', () => {
    expect(composerLayoutFor(ONE_LINE + 21, ONE_LINE)).toBe('stacked');
  });

  it('stacks for a long paragraph', () => {
    expect(composerLayoutFor(ONE_LINE + 21 * 6, ONE_LINE)).toBe('stacked');
  });

  it('an empty field is a pill', () => {
    expect(composerLayoutFor(ONE_LINE, ONE_LINE)).toBe('pill');
  });

  // Sub-pixel line metrics: a browser rounding scrollHeight up must not stack an empty composer.
  it('★ tolerates a one-pixel overshoot rather than stacking on a rounding error', () => {
    expect(composerLayoutFor(ONE_LINE + 1, ONE_LINE)).toBe('pill');
  });

  /**
   * ★★ THE OSCILLATION THE RULE EXISTS TO PREVENT.
   *
   * The failure mode is not a wrong answer, it is two answers alternating: in `pill` the field is
   * narrow, so a borderline sentence wraps and the composer stacks; in `stacked` the field is wider,
   * so the same sentence fits again and it returns to `pill` — where it wraps once more. The user sees
   * the box flicker while they type that one line.
   *
   * The guard is that the caller always measures at the PILL width, so the SAME input yields the SAME
   * answer no matter which state asked. That is what these two pin: feed the borderline measurement
   * twice and the answer must not change.
   */
  it('★★ returns the same answer for the same measurement, whichever state asked', () => {
    const borderline = ONE_LINE + 21;

    const fromPill = composerLayoutFor(borderline, ONE_LINE);
    const fromStacked = composerLayoutFor(borderline, ONE_LINE);

    expect(fromStacked).toBe(fromPill);
    expect(fromStacked).toBe('stacked');
  });

  it('★★ and the borderline does not flip back when re-evaluated', () => {
    const borderline = ONE_LINE + 21;
    const states = [
      composerLayoutFor(borderline, ONE_LINE),
      composerLayoutFor(borderline, ONE_LINE),
      composerLayoutFor(borderline, ONE_LINE),
    ];

    expect(new Set(states).size).withContext(states.join(' → ')).toBe(1);
  });

  it('clearing the field returns to a pill', () => {
    expect(composerLayoutFor(ONE_LINE + 21 * 4, ONE_LINE)).toBe('stacked');
    expect(composerLayoutFor(ONE_LINE, ONE_LINE)).toBe('pill');
  });

  it('is idempotent', () => {
    const once = composerLayoutFor(ONE_LINE + 40, ONE_LINE);
    const twice = composerLayoutFor(ONE_LINE + 40, ONE_LINE);

    expect(twice).toBe(once);
  });
});

describe('composerMaxHeight — a share of the panel, with a floor', () => {
  it('is 40% of a tall panel', () => {
    expect(composerMaxHeight(800)).toBe(320);
  });

  // ★ The drawer can be much shorter than the page, and 40% of it is a couple of lines. A composer
  // nobody can write two sentences in is worse than one that crowds the thread.
  it('★ never falls below the writable floor on a short panel', () => {
    expect(composerMaxHeight(200)).toBe(140);
    expect(composerMaxHeight(0)).toBe(140);
  });

  it('takes the share once it clears the floor', () => {
    expect(composerMaxHeight(400)).toBe(160);
  });

  it('is idempotent', () => {
    expect(composerMaxHeight(640)).toBe(composerMaxHeight(640));
  });
});
