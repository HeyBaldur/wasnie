/**
 * A hidden ruler for the composer: an off-screen twin of the textarea, used to ask how tall a piece of
 * text WOULD be — a question the live field cannot answer.
 *
 * ★ WHY NOT MEASURE THE REAL FIELD. The autosize writes an explicit `style.height` onto the textarea
 * (`WsTextareaComponent.resize`). From that moment its height is a value somebody chose, not a value
 * the content asked for, and reading it back to decide "does this need a second line?" answers with the
 * number that was just written. Four attempts at this composer failed on variations of that: measuring
 * the host instead of the field, measuring before layout, measuring an element whose width had been
 * momentarily forced. The lesson is not that the fourth bug needed fixing — it is that measuring a live,
 * styled, JS-sized element is fragile in more ways than anyone enumerates in advance.
 *
 * The mirror has no imposed height, ever. Its height is only ever its content's height.
 *
 * ★ AND ITS WIDTH IS ALWAYS THE PILL'S. The decision has to be monotone in the text's length or the
 * composer oscillates at the boundary: pill is narrow (the button sits beside the field) so the text
 * wraps and it stacks; stacked is wider, so the same text fits again and it un-stacks — forever, on one
 * exact sentence. Asking "would this fit on one line in the PILL?" in both directions removes that,
 * and the mirror can hold that width without disturbing anything the user is looking at.
 */
export class ComposerMirror {
  private element: HTMLElement | null = null;

  /**
   * Styles that decide where text wraps. Copied from the real field so the ruler and the thing it
   * measures agree — a mirror in a different font is a ruler with the wrong markings.
   */
  private static readonly COPIED_PROPERTIES = [
    'fontFamily', 'fontSize', 'fontWeight', 'fontStyle', 'fontVariant',
    'lineHeight', 'letterSpacing', 'wordSpacing', 'textTransform',
    'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft',
    'borderTopWidth', 'borderRightWidth', 'borderBottomWidth', 'borderLeftWidth',
    'boxSizing', 'wordBreak', 'overflowWrap', 'tabSize',
  ] as const;

  /**
   * How tall `text` would be in a field of `width` px, or null when the browser cannot answer.
   *
   * Null is not "zero" — see the caller: a measurement that did not happen must not be read as a short
   * one, because "short" is the answer that produces the bug.
   */
  measure(source: HTMLElement, text: string, width: number): number | null {
    if (width <= 0) {
      return null;
    }

    const mirror = this.ensure(source);
    mirror.style.width = `${width}px`;
    // ★ A TRAILING NEWLINE IS A LINE. Without a sentinel after it the browser collapses the final empty
    // line and a paragraph ending in Enter measures one line short — the composer would un-stack the
    // moment the user pressed Return at the end.
    mirror.textContent = `${text}\u200b`;

    const height = mirror.offsetHeight;
    return height > 0 ? height : null;
  }

  /** The height of a single line, from the SAME ruler — so the two numbers are always comparable. */
  measureSingleLine(source: HTMLElement, width: number): number | null {
    return this.measure(source, '', width);
  }

  /** Releases the ruler. Safe to call more than once. */
  destroy(): void {
    this.element?.remove();
    this.element = null;
  }

  /**
   * Builds the ruler if it is not there yet, and re-syncs its styles every time.
   *
   * ★ RE-SYNCED, NOT BUILT ONCE. A theme switch changes the font stack and the padding, and a ruler
   * copied at start-up would keep measuring in the old theme's metrics for the rest of the session —
   * silently, and only wrongly near the boundary.
   */
  private ensure(source: HTMLElement): HTMLElement {
    if (!this.element) {
      const mirror = document.createElement('div');
      mirror.setAttribute('aria-hidden', 'true');
      // Out of the layout, out of the accessibility tree, out of the tab order, and unable to affect
      // the page: it exists only to be measured.
      mirror.style.position = 'absolute';
      mirror.style.top = '0';
      mirror.style.left = '-99999px';
      mirror.style.visibility = 'hidden';
      mirror.style.pointerEvents = 'none';
      mirror.style.whiteSpace = 'pre-wrap';
      // Never a height: the whole point is that its height is the content's.
      mirror.style.height = 'auto';
      mirror.style.minHeight = '0';
      mirror.style.maxHeight = 'none';
      mirror.style.overflow = 'hidden';
      document.body.appendChild(mirror);
      this.element = mirror;
    }

    const computed = window.getComputedStyle(source);
    for (const property of ComposerMirror.COPIED_PROPERTIES) {
      // The list is a const tuple of real CSSStyleDeclaration keys; the cast is what lets
      // TypeScript accept indexing a type whose own index signature is read-only.
      (this.element.style as unknown as Record<string, string>)[property] = computed[property];
    }

    return this.element;
  }
}
