/**
 * TAKING THE MATHS OUT OF THE MARKDOWN, AND PUTTING IT BACK AFTER THE SANITISER.
 *
 * ★ THE PROBLEM. The assistant writes formulas in LaTeX — `\[ \text{Attainment} =
 * \frac{149000}{250000} = 0.596 \]` — and the panel renders Markdown, so the reader met backslashes and
 * braces. The calculation was right and unreadable, which to the person reading it is the same as wrong.
 *
 * ★ WHY THE PIPELINE IS NOT marked → KaTeX → SANITISER, which is the obvious order and does not work
 * here. The sanitiser at the end of this pipeline is ANGULAR'S, and it has no allowlist to extend: it
 * strips `style` attributes outright and knows nothing about MathML. KaTeX's output is built from inline
 * `style` (heights, vertical alignment, struts) and a MathML twin for screen readers — so running KaTeX
 * before the sanitiser produces beautifully rendered maths that arrives at the browser as a pile of
 * naked spans. Rendering after it, straight into the DOM, is the only order that survives, and it costs
 * nothing in safety: KaTeX is handed a TEX STRING and never HTML, it runs with `trust: false` so
 * `\href` and `\includegraphics` are disabled, and the model's own output is still fully sanitised on
 * the way through.
 *
 * ★ AND WHY THE MATHS IS EXTRACTED FIRST rather than found again at the end. `marked` runs with
 * `breaks: true`, so a block formula written over three lines comes out with `<br>` between them — the
 * opening `\[` and the closing `\]` end up in DIFFERENT TEXT NODES, and nothing looking for a pair can
 * find one. Markdown would also read `_` inside a formula as emphasis. Lifting the maths out before
 * Markdown ever sees it removes both problems at once, and what is left behind is a token made only of
 * lowercase letters and digits: nothing Markdown, and nothing a sanitiser, has any opinion about.
 */

/** The literal every token starts with — the cheap test for "is there any maths in here at all". */
export const MathTokenMarker = 'wsmath';

/**
 * A placeholder standing in for one formula: the marker, `b` for a block or `i` for inline, the TeX
 * hex-encoded, and a `z` to close it.
 *
 * ★ HEX, AND ONLY LOWERCASE LETTERS AND DIGITS, ON PURPOSE. The token has to cross a Markdown parser
 * and an HTML sanitiser without either of them recognising anything: base64 would bring `+`, `/` and
 * `=`, and an underscore would risk emphasis. `[0-9a-f]` cannot be mistaken for syntax by anything.
 * Formulas are short, so paying two characters per byte costs nothing worth optimising.
 */
export const MathTokenPattern = /wsmath([bi])([0-9a-f]*)z/g;

/** What a token decoded back to. */
export interface AssistantMath {
  readonly tex: string;
  readonly displayMode: boolean;
}

/**
 * The delimiter pairs recognised, in the order they are tried.
 *
 * ★ BLOCK BEFORE INLINE, so `$$…$$` is never read as two empty `$…$`.
 *
 * ★ AND `$…$` IS DELIBERATELY ABSENT. It is the classic inline delimiter and it is unusable in THIS
 * product: a sales-commission assistant writes "$1,200" and "$500" constantly, and a rule that treats a
 * dollar sign as the start of a formula turns money into gibberish — the exact failure this file exists
 * to remove, pointed at the amounts instead of the formulas. KaTeX's own auto-render omits it by default
 * for the same reason. `\(…\)` covers inline maths with no such ambiguity.
 */
const Delimiters: readonly { readonly pattern: RegExp; readonly displayMode: boolean }[] = [
  { pattern: /\\\[([\s\S]+?)\\\]/g, displayMode: true },
  { pattern: /\$\$([\s\S]+?)\$\$/g, displayMode: true },
  { pattern: /\\\(([\s\S]+?)\\\)/g, displayMode: false },
];

function toHex(value: string): string {
  return Array.from(new TextEncoder().encode(value))
    .map((byte) => byte.toString(16).padStart(2, '0'))
    .join('');
}

function fromHex(hex: string): string {
  const bytes = new Uint8Array(hex.length / 2);

  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = Number.parseInt(hex.slice(i * 2, i * 2 + 2), 16);
  }

  return new TextDecoder().decode(bytes);
}

/**
 * Replaces every formula in the model's reply with an inert token.
 *
 * ★ KNOWN AND ACCEPTED: a formula written INSIDE a fenced code block is extracted too, so a reply
 * demonstrating LaTeX source would render the demonstration instead of showing it. That is a model
 * explaining LaTeX to a user of a commissions product — a case nobody has had — and the alternative is
 * teaching this function to track fences, which is a Markdown parser growing inside a helper.
 */
export function protectMath(source: string): string {
  let result = source;

  for (const { pattern, displayMode } of Delimiters) {
    result = result.replace(pattern, (_match, tex: string) => {
      const trimmed = tex.trim();

      // An empty pair is punctuation the model happened to type, not a formula. Left exactly as it was.
      return trimmed.length === 0
        ? _match
        : `${MathTokenMarker}${displayMode ? 'b' : 'i'}${toHex(trimmed)}z`;
    });
  }

  return result;
}

/** Turns a token's captured groups back into the formula it stood for. */
export function decodeMathToken(mode: string, hex: string): AssistantMath {
  return { tex: fromHex(hex), displayMode: mode === 'b' };
}

/** Cheap enough to run on every change-detection pass; true only when there is work to do. */
export function containsMathToken(text: string | null | undefined): boolean {
  return !!text && text.includes(MathTokenMarker);
}
