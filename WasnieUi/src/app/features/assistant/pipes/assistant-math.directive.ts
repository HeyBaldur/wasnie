import { AfterViewChecked, Directive, ElementRef, inject } from '@angular/core';
import {
  MathTokenPattern,
  containsMathToken,
  decodeMathToken,
} from './assistant-math';

type Katex = typeof import('katex').default;

/**
 * Draws the formulas the pipe set aside, after Angular's sanitiser has had its say.
 *
 * ★ WHY IT RENDERS INTO THE DOM INSTEAD OF INTO THE HTML STRING. Angular's sanitiser is the last step
 * of the markdown pipeline and it has NO allowlist to extend: it removes `style` attributes and does
 * not know MathML — the two things KaTeX's output is built from. Maths rendered before it arrives as a
 * heap of naked spans. Rendering here, into the already-sanitised element, is the only order that
 * survives.
 *
 * ★ AND IT GIVES UP NOTHING IN SAFETY, which is the part worth being precise about. Nothing here parses
 * HTML from the model. KaTeX receives a TEX STRING — recovered from a token that is only lowercase
 * letters and digits — and builds nodes itself, with `trust: false`, so `\href`, `\includegraphics` and
 * every other markup-producing command are refused by KaTeX's own security model. The model's prose is
 * still escaped by `marked` and still passed through Angular's sanitiser exactly as before; this adds a
 * renderer, it does not open a door. There is no `bypassSecurityTrustHtml` here either.
 *
 * ★ AND A BROKEN FORMULA IS SHOWN, NOT THROWN. `throwOnError: false` makes KaTeX render an unparseable
 * expression as its own source in an error colour rather than raising — a model inventing a macro must
 * cost one ugly formula, never the whole reply.
 */
@Directive({
  selector: '[wsAssistantMath]',
  standalone: true,
})
export class AssistantMathDirective implements AfterViewChecked {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /**
   * KaTeX itself, fetched the first time a formula actually appears — and shared by every bubble.
   *
   * ★ WHY IT IS NOT A PLAIN IMPORT. The assistant panel lives in the app shell, not behind a route, so
   * anything it imports statically lands in the INITIAL bundle that every user downloads before seeing
   * a screen. KaTeX is a few hundred kilobytes of layout engine and most conversations contain no
   * maths at all — making every page load pay for a formula nobody asked for. A dynamic import moves it
   * to its own chunk, fetched the first time a `\[…\]` shows up and never again.
   */
  private static katex: Katex | null = null;

  private static loading: Promise<Katex> | null = null;

  /**
   * ★ AfterViewChecked, NOT ngOnChanges, and the reason is the binding it has to follow. The markup
   * arrives through `[innerHTML]` on the same element — a property binding, not an input of this
   * directive — so there is no change hook that fires for it. By the time the view has been checked the
   * HTML is in place, every time, including on each fragment of a streaming reply.
   *
   * The pass is guarded by a substring test on the element's text, so the ordinary case — a reply with
   * no maths in it — costs one scan and stops. Once a token has been rendered it is no longer in the
   * text, so re-checks are idempotent by construction rather than by a flag that could go stale.
   */
  ngAfterViewChecked(): void {
    const element = this.host.nativeElement;

    if (!containsMathToken(element.textContent)) {
      return;
    }

    if (AssistantMathDirective.katex !== null) {
      this.renderTokensIn(element, AssistantMathDirective.katex);
      return;
    }

    // ★ THE TOKENS STAY PUT UNTIL THE RENDERER ARRIVES, and that is the correct degradation: an
    // unresolved token is a short string of letters, not a broken formula and not a crash. When the
    // chunk lands, this renders straight into the DOM — no change detection is involved, because
    // nothing about the application's STATE changed, only the nodes inside one bubble.
    void AssistantMathDirective.load().then((katex) => this.renderTokensIn(element, katex));
  }

  private static load(): Promise<Katex> {
    AssistantMathDirective.loading ??= import('katex').then((module) => {
      AssistantMathDirective.katex = module.default;
      return module.default;
    });

    return AssistantMathDirective.loading;
  }

  /**
   * Walks the TEXT nodes only, and replaces the token inside them.
   *
   * ★ TEXT NODES, NOT innerHTML. Rewriting `innerHTML` would re-parse the whole bubble as HTML on every
   * pass — throwing away the sanitiser's work and re-creating every node, which in a streaming reply is
   * a flicker and in a long one is a lot of layout. Walking text nodes touches only what changed and
   * leaves the surrounding markup — the tables, the links the panel intercepts — physically untouched.
   */
  private renderTokensIn(root: HTMLElement, katex: Katex): void {
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    const pending: Text[] = [];

    for (let node = walker.nextNode(); node !== null; node = walker.nextNode()) {
      if (containsMathToken(node.nodeValue)) {
        pending.push(node as Text);
      }
    }

    // Collected first, replaced after: mutating the tree while a TreeWalker is standing in it is how a
    // walk starts skipping nodes.
    for (const node of pending) {
      AssistantMathDirective.replaceTokens(node, katex);
    }
  }

  private static replaceTokens(node: Text, katex: Katex): void {
    const text = node.nodeValue ?? '';
    const fragment = document.createDocumentFragment();
    let cursor = 0;

    // A fresh regex per call: the pattern is global, and a shared one carries `lastIndex` between
    // nodes — which silently skips the first formula of every second bubble.
    const pattern = new RegExp(MathTokenPattern.source, 'g');

    for (let match = pattern.exec(text); match !== null; match = pattern.exec(text)) {
      if (match.index > cursor) {
        fragment.append(text.slice(cursor, match.index));
      }

      fragment.append(AssistantMathDirective.render(match[1], match[2], katex));
      cursor = match.index + match[0].length;
    }

    if (cursor < text.length) {
      fragment.append(text.slice(cursor));
    }

    node.replaceWith(fragment);
  }

  private static render(mode: string, hex: string, katex: Katex): HTMLElement {
    const { tex, displayMode } = decodeMathToken(mode, hex);
    const host = document.createElement(displayMode ? 'div' : 'span');

    // The class is what the panel's stylesheet hangs the block spacing off; KaTeX adds its own.
    host.className = displayMode ? 'ws-math ws-math--block' : 'ws-math';

    katex.render(tex, host, {
      displayMode,
      // ★ The two settings that make this safe on model output, spelled out rather than left to the
      // library's defaults: no command may emit markup, and nothing may reach a URL.
      trust: false,
      // A formula the model got wrong shows as its own source, in the error colour. It does not throw,
      // and it does not take the answer down with it.
      throwOnError: false,
      output: 'htmlAndMathml',
    });

    return host;
  }
}
