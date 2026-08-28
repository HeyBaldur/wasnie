/**
 * The two pure pieces behind the bar under an assistant message: what "copy as text" copies, and how
 * the timestamp reads.
 */

/**
 * The message as the reader sees it, with no Markdown syntax left in it.
 *
 * ★ TAKEN FROM WHAT THE BROWSER ACTUALLY PAINTED, not by un-parsing the Markdown. A second parser
 * written to strip `#` and `**` would be a second implementation of the render — and the moment the two
 * disagree, "copy the answer" quietly hands over something the user never saw. `innerText` is the
 * browser's own answer to "what does this look like": it already collapses the markup, keeps the line
 * breaks a list or a table produced, and skips anything hidden.
 *
 * (`textContent` would NOT do: it ignores layout entirely and returns every cell of a table run
 * together on one line, plus the contents of anything CSS is hiding.)
 */
export function plainTextOf(rendered: HTMLElement | null | undefined): string {
  if (!rendered) {
    return '';
  }

  // innerText is layout-dependent, so it is undefined in environments that do not lay out; textContent
  // is the honest fallback there rather than an empty string.
  const text = rendered.innerText ?? rendered.textContent ?? '';
  return text.trim();
}

/**
 * When the turn was sent, absolute and in the reader's language.
 *
 * ★ ABSOLUTE, NOT RELATIVE. The project has a `relativeTime` pipe and this deliberately does not use
 * it: the value of a timestamp on a financial answer is being able to cite it later. "3 hours ago"
 * cannot be written down, and it means something different every time the page is re-read.
 *
 * ★ Intl, NOT Angular's DatePipe. Angular formats against the registered LOCALE_ID, and this app
 * registers none — so the pipe would print English month names to a Spanish or Polish reader whatever
 * the UI language says. `Intl.DateTimeFormat` takes the language as an argument and is in the browser
 * already, so the three languages come out right with no locale data to bundle.
 */
export function formatMessageTime(iso: string, language: string): string {
  const when = new Date(iso);
  if (Number.isNaN(when.getTime())) {
    return '';
  }

  return new Intl.DateTimeFormat(language || 'en', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(when);
}
