/**
 * The nearest ancestor that actually scrolls, starting from (and including) `from`.
 *
 * ★★ THE PAGE DOES NOT SCROLL IN `window`. It scrolls inside app-shell's own column
 * (`main.shell__content`, `overflow-y: auto`), which is why `window.scrollTo(0, 0)` is a no-op on
 * every screen in this app — a trap already documented in the manual's `jumpTo` and in the
 * date-picker's viewport listeners. Walking up and asking each ancestor whether it overflows keeps
 * this working if the shell's markup ever changes, instead of hard-coding a class name that would
 * fail silently the day it is renamed.
 *
 * ★ BOTH CONDITIONS ARE REQUIRED. An element can have `overflow-y: auto` and no overflow (nothing
 * to scroll), or overflow with `visible` (the ancestor scrolls it, not this one). Only an element
 * that is both is the scroller.
 *
 * ★ NOTE ON `overflow-y: visible`: CSS computes `overflow-y` to `auto` whenever `overflow-x` is not
 * `visible`, so a horizontally-scrolling table wrapper reports `auto` here too. It is skipped anyway
 * because it does not overflow VERTICALLY — which is why the height check is not redundant.
 *
 * @returns the scrolling element, or null when nothing up the tree scrolls.
 */
export function findScrollContainer(from: HTMLElement | null): HTMLElement | null {
  let el = from;

  while (el) {
    const overflowsVertically = el.scrollHeight > el.clientHeight + 1;
    if (overflowsVertically) {
      const overflowY = getComputedStyle(el).overflowY;
      if (overflowY === 'auto' || overflowY === 'scroll') {
        return el;
      }
    }
    el = el.parentElement;
  }

  return null;
}
