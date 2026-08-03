import { Pipe, PipeTransform, SecurityContext, inject } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { Marked } from 'marked';
import { internalRouteOf } from '../models/assistant.model';

/**
 * Renders an assistant message's Markdown as safe HTML.
 *
 * ★ WHY A LIBRARY AND NOT A FEW REGEXES. The assistant answers in Markdown and can produce anything a
 * model produces: nested lists, headings, fenced code, links, blockquotes and TABLES. A hand-rolled
 * parser would cover the two constructions someone thought of and break on the third — and the third
 * is a table, which is exactly what a question like "show me that as a table" returns.
 *
 * ★ SANITISED, TWICE, AND NEVER TRUSTED. This is output from a language model: Markdown permits inline
 * HTML, so an `<img onerror=...>` in a reply would run in the reader's browser. Two independent things
 * stop that:
 *
 *   1. `marked` is configured to ESCAPE raw HTML rather than pass it through, so the dangerous markup
 *      never becomes markup at all — it renders as visible text.
 *   2. The result goes through Angular's own sanitiser before it is returned, which strips scripts,
 *      event handlers and unsafe URLs from anything that slipped past the first step.
 *
 * There is no `bypassSecurityTrustHtml` anywhere in this file, and there must never be: that call is
 * the only way to make this unsafe, and it would look like a small convenience.
 */
@Pipe({
  name: 'assistantMarkdown',
  standalone: true,
})
export class AssistantMarkdownPipe implements PipeTransform {
  private readonly sanitizer = inject(DomSanitizer);

  /**
   * A private Marked instance rather than the global one: configuring the shared singleton would
   * change how Markdown renders anywhere else in the app that ever adopts it.
   */
  private readonly marked = new Marked({
    // GitHub-flavoured line breaks: a model writing a list often relies on single newlines, and
    // collapsing them turns a readable answer into a wall of text.
    breaks: true,
    gfm: true,
    renderer: {
      // ★ LAYER 1. Markdown permits inline HTML, and `marked` passes it through by default — so
      // `<img onerror=...>` in a reply would become a real element. This override escapes it instead:
      // the characters render as visible text and never become markup at all. (marked removed its own
      // `sanitize` option in v5; this is the supported replacement.)
      //
      // ★ WITH ONE EXCEPTION, AND IT IS EARNED. Escaping everything was too blunt for `<br>`: Markdown
      // has NO syntax for a line break inside a table cell, so every model reaches for the HTML tag —
      // and the reader saw a literal `<br>` in the middle of a table. See `IsAllowedRawHtml` for what
      // is let through and why the shape of the rule is what keeps it safe.
      html: ({ text }: { text: string }) =>
        AssistantMarkdownPipe.isAllowedRawHtml(text) ? text : AssistantMarkdownPipe.escape(text),
      // eslint-disable-next-line @typescript-eslint/naming-convention
      br: () => '<br>',
    },
  });

  /**
   * The one piece of raw HTML a model reply may keep: a bare line break.
   *
   * ★ WHY ONLY `<br>`. Markdown can already do bold, italics, code and links inside a table cell — a
   * model writing `<strong>` there is choosing the long way round, and nothing is lost by escaping it.
   * A LINE BREAK inside a cell is the one thing Markdown genuinely cannot express, which is why every
   * model emits the tag and why the reader met a literal `<br>` mid-table. One entry fixes the real
   * problem; a longer list would be surface area bought for no complaint.
   *
   * ★ WHY THE PATTERN IS THE SECURITY, not the tag name. It matches a bare tag and NOTHING else: no
   * attributes can appear inside it at all. That is what makes the rule safe rather than merely
   * narrow — the danger in HTML lives in attributes (`onerror`, `onclick`, `src`, `href`), and a
   * pattern with no room for one cannot carry a handler no matter which tag is listed. `<br onload=…>`
   * fails this test and is escaped like anything else.
   *
   * Angular's sanitiser still runs afterwards, so this is a narrowing of layer one, not a hole in it.
   */
  private static readonly AllowedRawHtml = /^<\s*br\s*\/?\s*>$/i;

  private static isAllowedRawHtml(value: string): boolean {
    return AssistantMarkdownPipe.AllowedRawHtml.test(value.trim());
  }

  /**
   * Every Unicode space that a model reaches for instead of a plain one.
   *
   * NO-BREAK, narrow no-break, thin, figure, the en/em quad family, medium mathematical and
   * ideographic. Markdown's list rule is defined against ASCII whitespace, so any of these after the
   * marker means the line is prose that happens to start with an asterisk.
   */
  private static readonly ExoticSpace = '[\u00A0\u2000-\u200A\u202F\u205F\u3000]';

  /**
   * Bullet and ordered markers separated from their text by a non-ASCII space.
   *
   * ★ ANCHORED TO THE LINE AND TO THE MARKER POSITION, and that narrowness is the safety. Up to three
   * leading spaces (what Markdown itself allows before a marker), then the marker, then exactly one
   * exotic space — which is replaced with a plain one. Nothing else in the line is touched, so a
   * no-break space used deliberately INSIDE a sentence ("10 000") survives untouched.
   */
  private static readonly BulletMarker =
    new RegExp(`^( {0,3}[*+-])${AssistantMarkdownPipe.ExoticSpace}`, 'gm');

  private static readonly OrderedMarker =
    // ★ THE DIGIT CLASS IS DOUBLE-ESCAPED, AND IT HAS TO BE. Inside a template literal an unrecognised
    // escape loses its backslash, so a single one would compile to a pattern matching the LETTER "d" —
    // silently, and only for ordered lists. The test that walks every marker is what caught it.
    new RegExp(`^( {0,3}\\d{1,9}[.)])${AssistantMarkdownPipe.ExoticSpace}`, 'gm');

  /**
   * Puts an ASCII space back between a list marker and its text.
   *
   * ★ THE PARSER WAS RIGHT AND THE INPUT WAS NOT. Reproduced against the real reply before touching
   * anything: `marked` renders `* item` as a list under this exact configuration, with `-` and `*`
   * alike, with or without a blank line before it. What arrives from the model is `*` followed by a
   * NO-BREAK SPACE, and Markdown's list rule is defined against ASCII whitespace — so the line is,
   * correctly, a paragraph that begins with an asterisk. That is why the screen showed
   * `<p>…<br>* How to view Plans…</p>`: raw markers and line breaks, no bullets.
   *
   * The same reply corroborates it: it also carries `–` and a non-breaking hyphen in "Step‑by‑step".
   * This model writes typographic Unicode, and the bullet's space is more of the same. No `marked`
   * option can fix that, because nothing is misconfigured — the text simply is not Markdown yet.
   *
   * ★ IT DOES NOT TOUCH `*emphasis*`. The pattern requires a SPACE after the marker; `*bold*` has a
   * letter there and never matches. (Nor could a match break emphasis even in principle: CommonMark
   * forbids an emphasis run from opening onto whitespace, so `*` followed by any space was never
   * going to be italics.)
   *
   * ★ AND IT DELIBERATELY LEAVES `*item` — no space at all — ALONE. That one is genuinely ambiguous
   * with emphasis: inserting a space would turn a line beginning an italic phrase into a bullet. A
   * repair that has to guess is not a repair.
   */
  private static repairListMarkers(value: string): string {
    return value
      .replace(AssistantMarkdownPipe.BulletMarker, '$1 ')
      .replace(AssistantMarkdownPipe.OrderedMarker, '$1 ');
  }

  /** Turns markup characters into the text a reader should see, rather than markup a browser runs. */
  private static escape(value: string): string {
    return value
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  transform(value: string | null | undefined): string {
    if (!value) {
      return '';
    }

    // ★ Layer 1 — raw HTML in the source is escaped, not rendered.
    //
    // `parse` is synchronous here because no async extension is registered; the cast documents that,
    // and a stray Promise would surface immediately as "[object Promise]" rather than silently.
    const html = this.marked.parse(
      AssistantMarkdownPipe.repairListMarkers(value), { async: false }) as string;

    const withSafeLinks = this.hardenLinks(
      AssistantMarkdownPipe.applyScrollbar(AssistantMarkdownPipe.renderTaskBoxes(html)),
    );

    // ★ Layer 2 — Angular's sanitiser has the final say.
    return this.sanitizer.sanitize(SecurityContext.HTML, withSafeLinks) ?? '';
  }

  /**
   * Puts the app's scrollbar on the two rendered elements that scroll.
   *
   * ★ REUSING THE UTILITY, NOT REPEATING IT. `ws-scroll-thin` is the design system's one scrollbar
   * (styles.scss), already worn by the sidebar, the modal body, the select list and the data table. A
   * code block or a wide table inside a chat bubble scrolls too, and was showing the browser's native
   * bar next to all of them.
   *
   * They are reached HERE rather than in the stylesheet because this markup is generated: there is no
   * template to put a class on, and copying the utility's rules into the panel's SCSS would be a second
   * definition of the same scrollbar — the drift this codebase keeps refusing.
   */
  private static applyScrollbar(html: string): string {
    return html
      .replace(/<pre>/gi, '<pre class="ws-scroll-thin">')
      .replace(/<table>/gi, '<table class="ws-scroll-thin">');
  }

  /**
   * Turns GFM task-list checkboxes into characters.
   *
   * ★ BECAUSE THE ELEMENT DOES NOT SURVIVE. `marked` renders `- [x] done` as an `<input
   * type="checkbox">`, and Angular's sanitiser removes form controls outright — correctly, they have
   * no business arriving from a model. The result was a checklist stripped of its boxes: "done" and
   * "pending" rendered as identical bullets, which is not a cosmetic loss but a WRONG reading of what
   * the assistant said.
   *
   * A character carries the same meaning, cannot be interacted with (these boxes were always
   * `disabled`), and passes any sanitiser because it is just text.
   */
  private static renderTaskBoxes(html: string): string {
    return html.replace(/<input([^>]*)type="checkbox"([^>]*)>/gi, (match) =>
      /\schecked\b/i.test(match) ? '☑ ' : '☐ ',
    );
  }

  /**
   * Every EXTERNAL link the assistant produces opens in a new tab, without handing that tab a
   * reference back. Links into Wasnie itself are left alone.
   *
   * `noopener` is the security half: without it the opened page can reach `window.opener` and navigate
   * the app to somewhere of its choosing. `noreferrer` keeps the address of the page the user was on
   * out of the request — a chat panel URL is nobody else's business.
   *
   * ★ WHY INTERNAL LINKS ARE EXEMPT. The assistant now guides with real routes (`/plans/new`), and the
   * panel intercepts those clicks to route through Angular so the app — and the conversation the user
   * is reading — survives. `target="_blank"` would fight that: the destination is the app the user is
   * already inside, and opening a second copy of it in a new tab is not what "go here" means.
   * `internalRouteOf` is the SAME rule the interceptor applies, so the two cannot disagree about which
   * links these are.
   *
   * Done by rewriting the emitted anchors rather than by overriding the renderer, because the
   * post-processing survives a `marked` upgrade changing its renderer API.
   */
  private hardenLinks(html: string): string {
    return html.replace(/<a\s+([^>]*?)>/gi, (match, attributes: string) => {
      const withoutOwn = attributes
        .replace(/\s*target\s*=\s*("[^"]*"|'[^']*'|\S+)/gi, '')
        .replace(/\s*rel\s*=\s*("[^"]*"|'[^']*'|\S+)/gi, '')
        .trim();

      const href = /\bhref\s*=\s*"([^"]*)"/i.exec(withoutOwn)?.[1] ?? null;

      return internalRouteOf(href) !== null
        ? `<a ${withoutOwn}>`
        : `<a ${withoutOwn} target="_blank" rel="noopener noreferrer">`;
    });
  }
}
