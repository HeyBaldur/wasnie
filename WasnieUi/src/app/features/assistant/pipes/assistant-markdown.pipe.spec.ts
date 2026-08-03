import { TestBed } from '@angular/core/testing';
import { AssistantMarkdownPipe } from './assistant-markdown.pipe';

/**
 * Rendering a language model's Markdown.
 *
 * ★ THE SECURITY TESTS ARE THE REASON THIS FILE EXISTS. Everything else here is presentation and would
 * be caught by looking at the screen. An `<img onerror=...>` that slips through would NOT be caught by
 * looking at the screen — it would look like nothing at all, right up until it ran.
 */
describe('AssistantMarkdownPipe', () => {
  let pipe: AssistantMarkdownPipe;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [AssistantMarkdownPipe] });
    pipe = TestBed.runInInjectionContext(() => new AssistantMarkdownPipe());
  });

  /** Renders into a detached element so the assertions can ask the DOM what was produced. */
  function render(markdown: string): HTMLElement {
    const host = document.createElement('div');
    host.innerHTML = pipe.transform(markdown);
    return host;
  }

  // ── 1. The full spectrum a model actually produces ────────────────────────

  it('renders lists, bold, links and TABLES as real elements', () => {
    const host = render(
      [
        '# Setting up a plan',
        '',
        'Follow **these** steps:',
        '',
        '1. Create the plan',
        '2. Add a rule',
        '   - flat rate',
        '   - accelerator',
        '',
        'See the [handbook](https://example.com/guide).',
        '',
        '| Tier | Rate |',
        '| --- | --- |',
        '| Base | 5% |',
        '| Accelerated | 8% |',
      ].join('\n'),
    );

    expect(host.querySelector('h1')?.textContent).toContain('Setting up a plan');
    expect(host.querySelector('strong')?.textContent).toBe('these');
    expect(host.querySelectorAll('ol > li').length).toBe(2);
    expect(host.querySelectorAll('ul > li').length).toBe(2, 'the nested bullets survive');
    expect(host.querySelector('a')?.getAttribute('href')).toBe('https://example.com/guide');

    // ★ The table — the construction a hand-rolled parser would have broken on, and the one Rodolfo
    // asked for by name.
    expect(host.querySelector('table')).toBeTruthy();
    expect(host.querySelectorAll('th').length).toBe(2);
    expect(host.querySelectorAll('tbody td').length).toBe(4);

    // And none of the source markers survive as literal text.
    expect(host.textContent).not.toContain('**');
    expect(host.textContent).not.toContain('| --- |');
  });

  it('renders inline code and fenced blocks', () => {
    const host = render('Use `PlanStatus` like:\n\n```csharp\nvar x = 1;\n```');

    expect(host.querySelector('code')?.textContent).toContain('PlanStatus');
    expect(host.querySelector('pre code')?.textContent).toContain('var x = 1;');
  });

  // ── 1a. THE WHOLE OF GFM ──────────────────────────────────────────────────
  //
  // A model can emit any of these at any moment, and each is a way the chat can look broken. Covered
  // exhaustively rather than by sampling: the constructions nobody thought to try are exactly the ones
  // that ship wrong — the reported `<br>` bug being the case in point.

  describe('every Markdown construction the assistant can emit', () => {
    it('renders all six heading levels', () => {
      const host = render('# h1\n\n## h2\n\n### h3\n\n#### h4\n\n##### h5\n\n###### h6');

      for (const level of [1, 2, 3, 4, 5, 6]) {
        expect(host.querySelector(`h${level}`)?.textContent).toBe(`h${level}`);
      }
    });

    it('renders bold, italic, bold-italic and strikethrough', () => {
      const host = render('**bold** _italic_ ***both*** ~~struck~~');

      expect(host.querySelector('strong')?.textContent).toBe('bold');
      expect(host.querySelector('em')?.textContent).toBe('italic');
      expect(host.querySelector('del')?.textContent).withContext('GFM strikethrough').toBe('struck');
      // ***both*** nests one inside the other, whichever way round marked chooses.
      expect(host.querySelector('strong em, em strong')).toBeTruthy();
    });

    it('renders fenced code with a language, and indented code', () => {
      const fenced = render('```typescript\nconst x: number = 1;\n```');
      expect(fenced.querySelector('pre code')?.textContent).toContain('const x: number = 1;');
      // The language lands as a class, which is what a highlighter would hook onto later.
      expect(fenced.querySelector('code')?.className).toContain('language-typescript');

      const indented = render('    indented code line');
      expect(indented.querySelector('pre code')?.textContent).toContain('indented code line');
    });

    it('renders blockquotes, including nested ones', () => {
      const host = render('> outer\n>\n> > inner');

      expect(host.querySelector('blockquote')).toBeTruthy();
      expect(host.querySelector('blockquote blockquote')?.textContent).toContain('inner');
    });

    it('renders ordered, unordered and deeply nested lists', () => {
      const host = render('1. first\n2. second\n   - nested\n     - deeper\n3. third');

      expect(host.querySelectorAll('ol > li').length).toBe(3);
      expect(host.querySelector('ol li ul li')?.textContent).toContain('nested');
      expect(host.querySelector('ol li ul li ul li')?.textContent).toContain('deeper');
    });

    it('renders a list that starts at a number other than one', () => {
      // A model writing step 3 of a procedure must not silently renumber to 1.
      const host = render('3. third\n4. fourth');
      expect(host.querySelector('ol')?.getAttribute('start')).toBe('3');
    });

    it('★ renders GFM task lists as characters, because the <input> does not survive', () => {
      // ★ Angular's sanitiser removes form controls — correctly; they have no business arriving from a
      // model. But that left "done" and "pending" rendering as identical bullets, which is not a
      // cosmetic loss: it is a WRONG reading of what the assistant said. Characters carry the same
      // meaning and pass any sanitiser.
      const host = render('- [x] done\n- [ ] pending');

      const items = Array.from(host.querySelectorAll('li')).map((li) => li.textContent?.trim());

      expect(items[0]).toContain('☑');
      expect(items[0]).toContain('done');
      expect(items[1]).toContain('☐');
      expect(items[1]).toContain('pending');

      // The distinction survives, which is the whole point.
      expect(items[0]).not.toContain('☐');
      expect(items[1]).not.toContain('☑');

      // And no interactive control reaches the page.
      expect(host.querySelector('input')).toBeNull();
    });

    it('renders table alignment and inline formatting inside cells', () => {
      const host = render(
        '| Left | Centre | Right |\n| :--- | :---: | ---: |\n| **a** | `b` | [c](https://example.com) |',
      );

      const headers = Array.from(host.querySelectorAll('th'));
      // Alignment arrives as inline style, which is why the stylesheet must not fight it.
      expect(headers[1].getAttribute('align') ?? headers[1].style.textAlign).toContain('center');
      expect(headers[2].getAttribute('align') ?? headers[2].style.textAlign).toContain('right');

      expect(host.querySelector('td strong')?.textContent).toBe('a');
      expect(host.querySelector('td code')?.textContent).toBe('b');
      expect(host.querySelector('td a')?.getAttribute('href')).toBe('https://example.com');
    });

    it('renders links of every spelling, including autolinks and titles', () => {
      const host = render(
        '[inline](https://a.example "titled") and <https://b.example> and https://c.example',
      );

      const hrefs = Array.from(host.querySelectorAll('a')).map((a) => a.getAttribute('href'));
      expect(hrefs).toContain('https://a.example');
      expect(hrefs).toContain('https://b.example');
      // GFM turns a bare URL into a link too.
      expect(hrefs).toContain('https://c.example');
      expect(host.querySelector('a[title]')?.getAttribute('title')).toBe('titled');
    });

    it('renders a horizontal rule', () => {
      expect(render('above\n\n---\n\nbelow').querySelector('hr')).toBeTruthy();
    });

    it('honours escaped characters and HTML entities', () => {
      const escaped = render(String.raw`\*not italic\* and \_not em\_`);
      expect(escaped.querySelector('em')).withContext('the backslash defuses the marker').toBeNull();
      expect(escaped.textContent).toContain('*not italic*');

      const entity = render('AT&amp;T and 5 &lt; 6');
      expect(entity.textContent).toContain('AT&T');
      expect(entity.textContent).toContain('5 < 6');
    });

    it('turns a single newline into a line break (GFM breaks)', () => {
      // `breaks: true` is deliberate: models write lists and addresses on consecutive lines and expect
      // them to stay apart. Without it the answer collapses into a wall of text.
      expect(render('line one\nline two').querySelector('br')).toBeTruthy();
    });

    it('renders a Markdown image, and neutralises a dangerous one', () => {
      // Markdown image syntax is legitimate and goes through marked's image renderer, not raw HTML.
      const ok = render('![a chart](https://example.com/chart.png)');
      expect(ok.querySelector('img')?.getAttribute('src')).toBe('https://example.com/chart.png');
      expect(ok.querySelector('img')?.getAttribute('alt')).toBe('a chart');

      // …but a script URL in the same position is not a picture.
      const bad = render('![x](javascript:alert(1))');
      expect(bad.querySelector('img')?.getAttribute('src')?.startsWith('javascript:')).toBeFalsy();
    });

    it('survives the constructions nested inside each other', () => {
      // The combinations are where a renderer usually gives up: a list holding a code block, a quote
      // holding a list, a table cell holding a link and a break.
      const host = render(
        [
          '1. Step with code:',
          '',
          '   ```sql',
          '   SELECT 1;',
          '   ```',
          '',
          '2. Step with a quote:',
          '',
          '   > be careful',
          '',
          '| Ref | Note |',
          '| --- | --- |',
          '| [doc](https://example.com) | one<br>two |',
        ].join('\n'),
      );

      expect(host.querySelector('ol li pre code')?.textContent).toContain('SELECT 1;');
      expect(host.querySelector('ol li blockquote')?.textContent).toContain('be careful');
      expect(host.querySelector('td a')).toBeTruthy();
      expect(host.querySelector('td br')).toBeTruthy();
    });
  });

  it("wears the design system's scrollbar on the elements that scroll", () => {
    // The utility is `ws-scroll-thin` from styles.scss — the same one the sidebar, the modal body and
    // the data table use. Applied here rather than in SCSS because this markup is generated: there is
    // no template to put a class on, and copying the rules would be a second scrollbar definition.
    const host = render('```\nlong line\n```\n\n| A | B |\n| --- | --- |\n| 1 | 2 |');

    expect(host.querySelector('pre')?.classList).toContain('ws-scroll-thin');
    expect(host.querySelector('table')?.classList).toContain('ws-scroll-thin');
  });

  // ── 1b. ★ THE ALLOWLIST: <br> survives, everything else still does not ────

  describe('★ raw <br> is the one tag that renders', () => {
    it('★ renders a line break inside a table cell instead of printing the tag', () => {
      // ★ THE REPORTED BUG. Markdown has NO syntax for a line break inside a table cell, so every model
      // reaches for the HTML tag — and blanket escaping showed the reader a literal `<br>` mid-table.
      const host = render('| Paso | Detalle |\n| --- | --- |\n| 1 | Abrí Planes<br>Elegí Nuevo |');

      const cell = host.querySelectorAll('tbody td')[1];
      expect(cell.querySelector('br')).withContext('a real line break, not text').toBeTruthy();
      expect(cell.textContent).not.toContain('<br>');
      expect(cell.textContent).toContain('Abrí Planes');
      expect(cell.textContent).toContain('Elegí Nuevo');
    });

    it('renders a loose <br> and its self-closing spellings', () => {
      for (const tag of ['<br>', '<br/>', '<br />', '<BR>', '<br >']) {
        const host = render(`line one${tag}line two`);
        expect(host.querySelector('br')).withContext(`${tag} must render`).toBeTruthy();
        expect(host.textContent).not.toContain('br>');
      }
    });

    it('★ a <br> carrying an ATTRIBUTE is escaped like any other raw HTML', () => {
      // ★ The pattern is the security, not the tag name: it has no room for an attribute, and
      // attributes are where handlers live. Listing `br` would be unsafe if the rule allowed
      // `<br onload=...>` through.
      const host = render('before<br onload="alert(1)">after');

      expect(host.querySelectorAll('*[onload]').length).toBe(0);
      expect(host.textContent).toContain('onload', 'shown as text, which is the safe outcome');
    });

    it('does not let a sibling tag ride in on the exception', () => {
      // Everything outside the one-entry list is still escaped.
      for (const markdown of ['<strong>bold</strong>', '<div>block</div>', '<span>x</span>', '<hr>']) {
        const host = render(markdown);
        expect(host.querySelector('strong,div,span,hr')).withContext(`${markdown} must not render`).toBeNull();
      }
    });
  });

  // ── 2. ★ SECURITY ─────────────────────────────────────────────────────────

  describe('★ output from a language model is never trusted', () => {
    it('does not turn a <script> tag into a script element', () => {
      const host = render('Here you go:\n\n<script>window.stolen = 1;</script>');

      expect(host.querySelector('script')).toBeNull('a reply must never introduce script');
      expect(pipe.transform('<script>alert(1)</script>')).not.toContain('<script');
    });

    it('★ does not turn an <img onerror> into an element that can fire', () => {
      // The nastier one: no <script> tag anywhere, and it still executes the moment it renders.
      const host = render('<img src="x" onerror="window.pwned = 1">');

      // ★ ASSERTED AGAINST THE DOM, not against the string. Escaping is a SUCCESS that leaves the
      // characters `onerror=` visible as text — searching the output for that substring would fail on
      // working code and pass on a renderer that deleted the payload silently. What matters is whether
      // the browser built an element out of it.
      expect(host.querySelector('img')).toBeNull('the payload must not become an element at all');
      expect(host.querySelectorAll('*[onerror]').length).toBe(0);
      expect(host.textContent).toContain('onerror', 'it is shown as text, which is the safe outcome');
    });

    it('strips event handlers, frames and javascript: URLs wherever they hide', () => {
      const cases = [
        '<div onclick="steal()">click</div>',
        '<iframe src="https://evil.example"></iframe>',
        '<svg><animate onbegin="alert(1)" /></svg>',
      ];

      for (const markdown of cases) {
        const host = render(markdown);

        expect(host.querySelectorAll('*[onclick], *[onbegin], *[onerror]').length)
          .toBe(0, `"${markdown}" must not produce a handler`);
        expect(host.querySelector('iframe')).toBeNull(`"${markdown}" must not embed a frame`);
      }
    });

    it('★ neutralises a javascript: link so clicking it cannot run anything', () => {
      // This one DOES survive as an anchor — Markdown link syntax is legitimate — so the defence is
      // the URL itself. Angular's sanitiser rewrites the scheme; the browser then treats the href as
      // an unknown relative URL rather than as code.
      const host = render('[click me](javascript:alert(1))');

      const link = host.querySelector('a');
      expect(link).toBeTruthy('the anchor itself is fine; its destination is what matters');
      expect(link!.getAttribute('href')?.startsWith('javascript:'))
        .withContext('a script URL must never survive as the destination')
        .toBeFalse();
    });

    it('shows escaped HTML as visible text rather than swallowing it silently', () => {
      // Escaping, not deleting: if a model explains an HTML tag, the user should SEE the tag.
      const host = render('The tag `<b>` makes text bold.');

      expect(host.textContent).toContain('<b>');
      expect(host.querySelector('code b')).toBeNull();
    });
  });

  // ── 3. Links open without handing over the window ─────────────────────────

  it('★ every EXTERNAL link carries rel="noopener noreferrer" and opens in a new tab', () => {
    const host = render('See [the guide](https://example.com) and [another](https://other.example).');

    const links = Array.from(host.querySelectorAll('a'));
    expect(links.length).toBe(2);

    for (const link of links) {
      // noopener: without it the opened page can reach window.opener and navigate this app away.
      // noreferrer: the address the user was on is nobody else's business.
      expect(link.getAttribute('rel')).toBe('noopener noreferrer');
      expect(link.getAttribute('target')).toBe('_blank');
    }
  });

  it('★ a link INTO Wasnie is left for the app to route, not opened in a new tab', () => {
    // ★ The assistant guides with real routes now. `target="_blank"` on one of them would fight the
    // panel's interceptor for the same click, and would mean "go to /plans/new" opened a SECOND copy
    // of the app the user is already inside.
    const host = render('Then click [New Plan](/plans/new).');

    const link = host.querySelector('a')!;
    expect(link.getAttribute('href')).toBe('/plans/new');
    expect(link.getAttribute('target')).toBeNull();
    expect(link.getAttribute('rel')).toBeNull();
  });

  it('★ a protocol-relative URL is treated as EXTERNAL despite its leading slash', () => {
    // `//evil.com` is not a path — the browser resolves it to `https://evil.com`. It must keep the
    // external hardening, and the interceptor must leave it to the browser. Both sides ask the same
    // `internalRouteOf`, so this and the panel's twin test cannot drift apart.
    const host = render('[looks internal](//evil.com/x)');

    const link = host.querySelector('a')!;
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
  });

  it('overrides a target the model tried to set for itself', () => {
    const host = render('<a href="https://example.com" target="_self" rel="opener">x</a>\n\n[ok](https://example.com)');

    for (const link of Array.from(host.querySelectorAll('a'))) {
      expect(link.getAttribute('rel')).toBe('noopener noreferrer');
    }
  });

  // ── 4. Partial Markdown, mid-stream ───────────────────────────────────────

  it('renders half-arrived Markdown without throwing', () => {
    // Every prefix of a real answer is rendered at some point while it streams. None may break.
    const answer = 'Steps:\n\n1. **Create** the plan\n2. Add a `rule`\n\n| A | B |\n| --- | --- |\n| 1 | 2 |';

    for (let i = 1; i <= answer.length; i++) {
      expect(() => pipe.transform(answer.slice(0, i))).not.toThrow();
    }

    // An unclosed marker is simply not bold YET — it shows as characters and resolves on the next
    // fragment, which is why nothing has to detect "the end of the stream".
    expect(pipe.transform('**not closed yet')).toContain('**not closed yet');
    expect(pipe.transform('**now closed**')).toContain('<strong>now closed</strong>');
  });

  it('returns empty for empty input rather than rendering a stray paragraph', () => {
    expect(pipe.transform('')).toBe('');
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
  });
});
