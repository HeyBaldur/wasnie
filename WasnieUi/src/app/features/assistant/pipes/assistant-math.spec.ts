import { Component, ViewChild, ElementRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AssistantMarkdownPipe } from './assistant-markdown.pipe';
import { AssistantMathDirective } from './assistant-math.directive';
import { MathTokenMarker } from './assistant-math';

/**
 * MATHS THE READER CAN ACTUALLY SEE.
 *
 * ★ THE SYMPTOM. The assistant answered with
 * `\[ \text{Attainment fraction} = \frac{149000}{250000} = 0.596 \]` and the panel, which renders
 * Markdown and nothing else, showed exactly those characters. The arithmetic was right; to the person
 * reading it, backslashes and braces are the same as being wrong.
 *
 * ★ WHAT THESE TESTS PIN. Two halves that must hold together: the pipe has to take the formula OUT
 * before Markdown mangles it, and the directive has to put it BACK after Angular's sanitiser — which is
 * the only order that survives, because that sanitiser strips the inline styles KaTeX is built from and
 * has no allowlist to extend. And neither half may cost the security the pipe already had.
 */
describe('assistant maths', () => {
  const blockFormula = String.raw`\[ \text{Attainment} = \frac{149000}{250000} = 0.596 \]`;
  const inlineFormula = String.raw`\( 0.08 \times 149000 \)`;

  // ── The pipe: the formula leaves before Markdown can touch it ──────────────

  describe('AssistantMarkdownPipe — maths is lifted out before Markdown', () => {
    let pipe: AssistantMarkdownPipe;

    beforeEach(() => {
      TestBed.configureTestingModule({ providers: [AssistantMarkdownPipe] });
      pipe = TestBed.runInInjectionContext(() => new AssistantMarkdownPipe());
    });

    it('★ replaces a block formula with a token, so no LaTeX reaches the reader', () => {
      const html = pipe.transform(`El attainment es:\n\n${blockFormula}\n\ny la comisión sale de ahí.`);

      expect(html).toContain(MathTokenMarker);
      expect(html).not.toContain('frac');
      expect(html).not.toContain('\\[');
      // The prose around it is still ordinary Markdown.
      expect(html).toContain('El attainment es');
    });

    it('replaces an inline formula too', () => {
      const html = pipe.transform(`La base es ${inlineFormula} euros.`);

      expect(html).toContain(MathTokenMarker);
      expect(html).not.toContain('times');
      expect(html).toContain('La base es');
    });

    it('★ the token survives Markdown AND the sanitiser intact', () => {
      // ★ THE WHOLE DESIGN RESTS ON THIS. The token is lowercase letters and digits precisely so that
      // neither the parser nor the sanitiser has an opinion about it. If either mangled it, the
      // directive would find nothing and the formula would vanish instead of rendering.
      const html = pipe.transform(`- punto\n\n${blockFormula}\n`);
      const token = /wsmath[bi][0-9a-f]+z/.exec(html);

      expect(token).not.toBeNull();
      // Round-trips: the hex decodes back to the formula the model wrote.
      const hex = /wsmath[bi]([0-9a-f]+)z/.exec(html)![1];
      const bytes = new Uint8Array(hex.length / 2);
      for (let i = 0; i < bytes.length; i++) {
        bytes[i] = Number.parseInt(hex.slice(i * 2, i * 2 + 2), 16);
      }
      expect(new TextDecoder().decode(bytes)).toContain('\\frac{149000}{250000}');
    });

    it('★ a multi-line block formula is not split by the line-break setting', () => {
      // ★ THE REASON THE FORMULA IS EXTRACTED BEFORE MARKDOWN RATHER THAN FOUND AFTERWARDS. `marked`
      // runs with breaks: true, so a formula written over three lines would come out with <br> between
      // them — its opening and closing delimiters in DIFFERENT text nodes, where nothing can pair them.
      const html = pipe.transform(
        String.raw`\[` + '\n' + String.raw`\frac{149000}{250000}` + '\n' + String.raw`\]`);

      expect((html.match(/wsmath/g) ?? []).length).toBe(1);
      expect(html).not.toContain('frac');
    });

    it('★ a dollar amount is NOT read as a formula', () => {
      // ★ THE DELIMITER THIS PRODUCT CANNOT HAVE. `$…$` is the classic inline spelling and it is
      // unusable in a commissions assistant: "$1,200" and "$500" in one sentence would be read as a
      // formula between them, and the money would turn to gibberish.
      const html = pipe.transform('La comisión pasó de $1,200 a $500 este mes.');

      expect(html).not.toContain(MathTokenMarker);
      expect(html).toContain('$1,200');
      expect(html).toContain('$500');
    });

    it('leaves an empty delimiter pair alone, because it is punctuation and not a formula', () => {
      const html = pipe.transform(String.raw`escribe \[\] para nada`);

      expect(html).not.toContain(MathTokenMarker);
    });

    it('★ ordinary Markdown is untouched by any of this', () => {
      const host = document.createElement('div');
      host.innerHTML = pipe.transform(
        ['| A | B |', '| --- | --- |', '| 1 | 2 |', '', '- viñeta', '', '[Planes](/plans)'].join('\n'),
      );

      expect(host.querySelector('table')).not.toBeNull();
      expect(host.querySelector('li')?.textContent).toContain('viñeta');
      expect(host.querySelector('a')?.getAttribute('href')).toBe('/plans');
      // An internal link keeps no target, so the panel's click interceptor still routes it.
      expect(host.querySelector('a')?.getAttribute('target')).toBeNull();
    });

    it('★ the XSS posture is unchanged by the maths path', () => {
      const html = pipe.transform(
        `<script>alert(1)</script>\n\n${blockFormula}\n\n<img src=x onerror="alert(1)">`);
      const host = document.createElement('div');
      host.innerHTML = html;

      expect(host.querySelector('script')).toBeNull();
      expect(host.querySelector('img')).toBeNull();
      expect(html).not.toContain('onerror=');
      // And the formula still left as a token, so nothing was lost to the escaping.
      expect(html).toContain(MathTokenMarker);
    });

    it('★ a formula cannot smuggle markup through the token', () => {
      // The TeX is hex-encoded, so even a formula full of angle brackets arrives as digits — there is
      // no path by which model text becomes markup here.
      const html = pipe.transform(String.raw`\[ <script>alert(1)</script> \]`);

      expect(html).not.toContain('<script');
      expect(html).toContain(MathTokenMarker);
    });
  });

  // ── The directive: the formula comes back, drawn ───────────────────────────

  @Component({
    standalone: true,
    imports: [AssistantMarkdownPipe, AssistantMathDirective],
    template: '<div #host wsAssistantMath [innerHTML]="source | assistantMarkdown"></div>',
  })
  class HostComponent {
    source = '';
    @ViewChild('host') el!: ElementRef<HTMLElement>;
  }

  describe('AssistantMathDirective — the formula is drawn after the sanitiser', () => {
    let fixture: ComponentFixture<HostComponent>;

    beforeEach(() => {
      TestBed.configureTestingModule({ imports: [HostComponent] });
      fixture = TestBed.createComponent(HostComponent);
    });

    /**
     * KaTeX is fetched on demand — it is a few hundred kilobytes and the panel lives in the app shell,
     * so a static import would put it in the bundle every user downloads before seeing a screen. The
     * test therefore has to await that chunk, exactly as the browser does.
     */
    async function renderMaths(source: string): Promise<HTMLElement> {
      fixture.componentInstance.source = source;
      fixture.detectChanges();
      await import('katex');
      await fixture.whenStable();
      fixture.detectChanges();
      return fixture.componentInstance.el.nativeElement;
    }

    it('★ draws a BLOCK formula as KaTeX markup instead of showing its source', async () => {
      const host = await renderMaths(blockFormula);

      expect(host.querySelector('.katex')).not.toBeNull();
      expect(host.querySelector('.ws-math--block')).not.toBeNull();
      expect(host.textContent).not.toContain('frac');
      expect(host.textContent).not.toContain(MathTokenMarker);
    });

    it('draws an INLINE formula, inside the sentence it belongs to', async () => {
      const host = await renderMaths(`La base es ${inlineFormula} euros.`);

      expect(host.querySelector('.katex')).not.toBeNull();
      expect(host.querySelector('.ws-math--block')).toBeNull();
      expect(host.textContent).toContain('La base es');
      expect(host.textContent).toContain('euros.');
    });

    it('★ leaves the surrounding markup physically untouched', async () => {
      // The directive walks text nodes rather than rewriting innerHTML: the table and the link — which
      // the panel intercepts clicks on — must be the same nodes they were before.
      const host = await renderMaths(
        ['| A | B |', '| --- | --- |', '| 1 | 2 |', '', blockFormula, '', '[Planes](/plans)'].join('\n'),
      );

      expect(host.querySelector('table')).not.toBeNull();
      expect(host.querySelector('a')?.getAttribute('href')).toBe('/plans');
      expect(host.querySelector('.katex')).not.toBeNull();
    });

    it('★ a formula KaTeX cannot parse shows its source instead of taking the answer down', async () => {
      const host = await renderMaths(String.raw`\[ \noSuchMacro{1} \]`);

      expect(host.querySelector('.katex-error')).not.toBeNull();
      // The rest of the reply is still there — one bad formula is not a broken message.
      expect(host.textContent).toContain('noSuchMacro');
    });

    it('does nothing at all to a reply with no maths in it', async () => {
      const host = await renderMaths('Solo texto, **negrita** y una [ruta](/plans).');

      expect(host.querySelector('.katex')).toBeNull();
      expect(host.querySelector('strong')).not.toBeNull();
    });
  });
});
