/**
 * Which shape the composer takes: a one-line pill, or a stack with the button on its own row.
 *
 * `pill`    — the text fits on one line; textarea and send button share the row.
 * `stacked` — the text needs more than one line; the textarea takes the full width and the button
 *             drops to a row of its own underneath.
 */
export type ComposerLayout = 'pill' | 'stacked';

/**
 * Decides the shape from a MEASUREMENT, never from the text.
 *
 * ★ NOT A CHARACTER OR NEWLINE COUNT. A paragraph with no line break at all still wraps, and a short
 * string with an early newline does not — counting either one gets the common case wrong.
 *
 * ★★ AND THE MEASUREMENT MUST ALWAYS BE THE NARROW ONE. This is the whole reason the rule is written
 * as a function instead of an inline comparison. Decide "does it overflow?" against whatever width the
 * box happens to have right now and the composer oscillates at the boundary: in `pill` the field is
 * narrow (the button is beside it) so the text wraps → `stacked`; in `stacked` the field is WIDER, so
 * the same text now fits on one line → back to `pill` → it wraps again → flicker, forever, on exactly
 * one line of text. It is easy to miss in development and impossible to miss for the user typing that
 * sentence.
 *
 * So the caller measures the content at the PILL width in both directions, and this only compares. The
 * criterion is then monotone in the text's length, and a state it entered it can only leave by the
 * same threshold it crossed to get there.
 *
 * @param contentHeight   the field's scrollHeight measured at the PILL width, in px
 * @param singleLineHeight the height of that same field holding exactly one line, in px
 */
export function composerLayoutFor(contentHeight: number, singleLineHeight: number): ComposerLayout {
  // A hair of tolerance: sub-pixel line metrics and a browser that rounds scrollHeight up would
  // otherwise report a one-line field as one pixel too tall and stack an empty composer.
  return contentHeight > singleLineHeight + 1 ? 'stacked' : 'pill';
}

/**
 * The composer's height ceiling: a share of the panel it sits in, never of the viewport.
 *
 * ★ THE PANEL, NOT THE WINDOW. The drawer is a 420px-wide slide-over that can be much shorter than the
 * page; a ceiling expressed against the viewport would let the composer eat a short drawer whole while
 * barely showing up on a tall page. The container is the thing the user is actually reading in.
 *
 * ★ AND A FLOOR, because a share is meaningless on a small panel: 40% of a short drawer is a couple of
 * lines, and a composer you cannot write two sentences in is worse than one that crowds the thread.
 *
 * @param panelHeight the height of the scrolling panel that contains the conversation, in px
 */
export function composerMaxHeight(panelHeight: number): number {
  const share = Math.round(panelHeight * COMPOSER_MAX_SHARE);
  return Math.max(COMPOSER_MIN_CEILING_PX, share);
}

/** The share of the panel the composer may occupy at most. */
const COMPOSER_MAX_SHARE = 0.4;

/**
 * The lowest the ceiling may fall, in px — roughly four lines plus the button's row. Below this the
 * control stops being writable, which no proportion is worth.
 */
const COMPOSER_MIN_CEILING_PX = 140;
