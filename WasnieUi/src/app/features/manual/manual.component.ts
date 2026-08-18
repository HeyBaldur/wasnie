import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslatePipe } from '@ngx-translate/core';
import { Marked } from 'marked';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { WsCardComponent, WsButtonComponent, WsEmptyStateComponent } from '../../shared/ui';
import { ManualApiService } from './services/manual.api.service';
import { ManualHeading } from './models/manual.model';
import { AssistantStore } from '../assistant/state/assistant.store';
import { WelcomeService } from '../../core/services/welcome.service';

type ManualState = 'loading' | 'ready' | 'unavailable' | 'error';

/**
 * The id a heading gets, matching the slug Markdown tooling already assumes.
 *
 * ★ THIS IS NOT A STYLE CHOICE — IT IS WHAT MAKES THE DOCUMENT'S OWN LINKS WORK. The guide is full of
 * internal references written by hand: `[Rate tables](#5-rate-tables)`,
 * `[4.5](#45-plan-lifecycle-and-assignments)`, `[15.1](#151-configuration-the-policy-is-opt-in-per-plan)`.
 * Inventing our own id scheme left every one of them pointing at nothing — a document full of links that
 * look right and go nowhere.
 *
 * The rule was derived from those real anchors, not guessed: lowercase, drop punctuation entirely (so the
 * dot in "4.5" disappears and it becomes `45`), turn spaces into hyphens. "6. SplitAtQuota — the
 * accelerator question" gives `6-splitatquota--the-accelerator-question`, double hyphen and all, because
 * the em dash vanishes between two spaces.
 */
function slugify(text: string): string {
  return text
    .toLowerCase()
    .replace(/[^\p{L}\p{N} -]/gu, '')
    .trim()
    .replace(/ /g, '-');
}

@Component({
  selector: 'app-manual',
  standalone: true,
  imports: [AppShellComponent, TranslatePipe, WsCardComponent, WsButtonComponent, WsEmptyStateComponent],
  templateUrl: './manual.component.html',
  styleUrl: './manual.component.scss',
})
export class ManualComponent implements OnInit {
  private readonly api = inject(ManualApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  /** Root-provided, so a button here opens the panel that lives in the shell. */
  readonly assistant = inject(AssistantStore);
  // The modal itself is rendered by the app shell; this only flips the shared signal.
  readonly welcome = inject(WelcomeService);

  private readonly docRef = viewChild<ElementRef<HTMLElement>>('doc');

  readonly state = signal<ManualState>('loading');
  readonly html = signal('');
  readonly headings = signal<ManualHeading[]>([]);

  /**
   * The navigation: the document's numbered chapters, in document order.
   *
   * ★ DERIVED, NEVER LISTED. A hand-written menu of four entries described a guide that has eighteen, and
   * it would have described the wrong ones the moment the markdown changed — which it will, since the
   * markdown is the source of truth and is edited by hand. This reads the headings that were actually
   * rendered, so the menu cannot disagree with the document: add a chapter and it appears, rename one and
   * the label follows, delete one and it goes.
   *
   * ★ NUMBERED ONLY, USING THE DOCUMENT'S OWN CONVENTION RATHER THAN A LIST OF EXCEPTIONS. The guide
   * numbers what a reader is meant to navigate ("1.", "2." … "18.") and leaves its own housekeeping
   * unnumbered: "Coverage checklist", "How to read this document", "Maintenance" — that last one is an
   * instruction to whoever maintains the FILE ("re-verify this document against the code"), not a chapter
   * about Wasnie. Filtering on the numbering keeps them out of a menu meant for someone hunting an
   * answer, without hard-coding a single title: add a section 19 and it shows up on its own.
   *
   * ★ THE COST, STATED: a real chapter written WITHOUT a number would be missing from this menu. It stays
   * in the document and is still reachable by scrolling, so nothing is lost — but if unnumbered chapters
   * ever become normal, this rule is what has to change.
   */
  readonly chapters = computed(() =>
    this.headings().filter(h => h.level === 2 && /^\d+[.\s]/.test(h.text)),
  );

  /**
   * ★ NO `bypassSecurityTrust…` ANYWHERE HERE. Binding a plain string to [innerHTML] runs Angular's own
   * sanitiser, which is the right default even though the markdown comes from our own repository: the
   * document travels over the network, and "we trust the source" is an assumption that only has to be
   * wrong once. Heading ids are attached afterwards, in the DOM, so nothing depends on attributes
   * surviving sanitisation.
   */
  private readonly marked = new Marked({ gfm: true, breaks: false });

  private pdfUrl: string | null = null;

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => this.release());
    this.load();
  }

  load(): void {
    this.release();
    this.state.set('loading');

    this.api.getContent().subscribe({
      next: content => {
        this.html.set(this.sectionize(this.marked.parse(content.markdown, { async: false }) as string));
        this.state.set('ready');
        afterNextRender(() => this.indexHeadings(), { injector: this.injector });
      },
      error: (err: HttpErrorResponse) => {
        // 404 is "no manual on this installation", which is an expected state with its own calm screen —
        // not the same thing as a failure, which offers a retry.
        this.state.set(err.status === 404 ? 'unavailable' : 'error');
      },
    });
  }

  /**
   * Cuts the flat markdown output into one CARD PER CHAPTER.
   *
   * ★ WHY THE DOM AND NOT A `marked` RENDERER. A renderer emits tokens one at a time and never learns
   * where a chapter ENDS — it would have to open a `<section>` on one heading and close it on the next,
   * which means emitting unbalanced HTML and hoping the parser repairs it. Grouping after the fact is the
   * same work stated honestly: walk the top-level nodes once, start a new section at every `h2`.
   *
   * ★ EVERYTHING STAYS SANITISER-SAFE. Only `<section>` and `<span>` with a `class` are introduced — all
   * of it survives Angular's sanitiser, which is still the thing that renders this ([innerHTML] with a
   * plain string). No `bypassSecurityTrust…` was needed and none was added.
   *
   * ★ AND THE HEADING'S `textContent` IS LEFT BYTE-FOR-BYTE IDENTICAL. That is not a detail: the ids come
   * from `slugify(el.textContent)`, and the guide's own hand-written links (`[4.5](#45-plan-lifecycle…)`)
   * point at those slugs. Moving "1." into a badge span keeps it inside the heading, in the same order, so
   * the concatenation the slug is built from does not change. The dot is HIDDEN with `display: none`
   * rather than removed for exactly this reason — CSS is invisible to `textContent`.
   */
  private sectionize(html: string): string {
    const doc = new DOMParser().parseFromString(html, 'text/html');
    const out = doc.createElement('div');
    let current: HTMLElement | null = null;

    const open = (modifier: string): HTMLElement => {
      const section = doc.createElement('section');
      section.className = modifier ? `manual__chapter manual__chapter--${modifier}` : 'manual__chapter';
      out.appendChild(section);
      return section;
    };

    // `Array.from` first: appending a node MOVES it out of `body.childNodes`, so iterating the live list
    // would skip every second child.
    for (const node of Array.from(doc.body.childNodes)) {
      if (node.nodeType === Node.ELEMENT_NODE && (node as Element).tagName === 'H2') {
        current = open('');
        this.decorateChapterHeading(node as HTMLElement, doc);
      } else if (!current) {
        // The title and whatever precedes the first chapter — its own card, so nothing is left loose on
        // the page background.
        current = open('lead');
      }
      current.appendChild(node);
    }

    return out.innerHTML;
  }

  /**
   * Gives a chapter heading its visual anchor.
   *
   * The guide numbers the chapters a reader navigates ("1." … "18.") and leaves its own housekeeping
   * unnumbered ("Coverage checklist", "Maintenance") — the same convention the shortcut menu already
   * reads. Numbered chapters get the number as a badge; the housekeeping sections get an icon in the same
   * slot, so every card opens on the same geometry.
   */
  private decorateChapterHeading(heading: HTMLElement, doc: Document): void {
    const match = /^(\d+)(\.)/.exec(heading.textContent ?? '');
    const badge = doc.createElement('span');

    if (match) {
      badge.className = 'manual__chapter-num';
      badge.appendChild(doc.createTextNode(match[1]));

      const dot = doc.createElement('span');
      dot.className = 'manual__chapter-dot';
      dot.appendChild(doc.createTextNode(match[2]));
      badge.appendChild(dot);

      // Remove only the characters the badge now carries, from the text node that held them.
      const first = heading.firstChild;
      if (first?.nodeType === Node.TEXT_NODE) {
        first.textContent = (first.textContent ?? '').slice(match[0].length);
      }
    } else {
      // Empty on purpose: the icon is a CSS mask, so it adds nothing to `textContent` and cannot disturb
      // the slug.
      badge.className = 'manual__chapter-num manual__chapter-num--icon';
    }

    heading.insertBefore(badge, heading.firstChild);
  }

  /**
   * Gives every chapter and sub-section an id, and collects them.
   *
   * Ids are derived from the heading's own text, so an anchor cannot point at a section that is not
   * there: the list and the document are built from the same DOM, in the same pass.
   */
  private indexHeadings(): void {
    const host = this.docRef()?.nativeElement;
    if (!host) {
      return;
    }

    const found: ManualHeading[] = [];
    const used = new Set<string>();

    host.querySelectorAll<HTMLHeadingElement>('h2, h3, h4').forEach(el => {
      const text = (el.textContent ?? '').trim();
      if (!text) {
        return;
      }

      // Two headings can slugify identically; the document's own links point at the FIRST, which is the
      // convention Markdown tooling follows, so later duplicates get the suffix rather than the first.
      let id = slugify(text);
      let suffix = 1;
      while (used.has(id)) {
        id = `${slugify(text)}-${suffix++}`;
      }
      used.add(id);

      el.id = id;
      found.push({ id, level: Number(el.tagName.slice(1)), text });
    });

    this.headings.set(found);
  }

  /**
   * ★ THE DOCUMENT'S OWN LINKS, MADE TO WORK. The guide cross-references itself constantly, and a plain
   * `#anchor` would ask the WINDOW to jump — which does nothing useful here, because the scrolling
   * element is the article, not the page. Catching the click on the way up lets the right container do
   * the scrolling, and leaves external links alone.
   */
  onDocumentClick(event: MouseEvent): void {
    const anchor = (event.target as HTMLElement | null)?.closest?.('a[href]') as HTMLAnchorElement | null;
    const href = anchor?.getAttribute('href');
    if (!href?.startsWith('#')) {
      return;
    }

    event.preventDefault();
    this.jumpTo(decodeURIComponent(href.slice(1)));
  }

  /**
   * Scrolls the DOCUMENT column, not the window.
   *
   * Measured with rectangles rather than `offsetTop`: the headings' offset parent is not necessarily the
   * scroller, and `scrollIntoView` would move whichever ancestor it felt like — including the page.
   */
  jumpTo(id: string): void {
    const host = this.docRef()?.nativeElement;
    if (!host || !id) {
      return;
    }

    let target: HTMLElement | null = null;
    try {
      target = host.querySelector<HTMLElement>(`#${CSS.escape(id)}`);
    } catch {
      // An id from the document that is not a usable selector: nothing to jump to, and not worth throwing.
      return;
    }
    if (!target) {
      return;
    }

    // ★ A CHAPTER HEADING OPENS ITS CARD, so scrolling to the heading itself would park the card's top
    // edge — border, radius and all — just above the viewport, and a card sliced off at the top reads as
    // a rendering bug. When the heading is the first thing in its card, the card is what we scroll to.
    const parent = target.parentElement;
    const anchor =
      parent?.classList.contains('manual__chapter') && target === parent.firstElementChild
        ? parent
        : target;

    const top = anchor.getBoundingClientRect().top - host.getBoundingClientRect().top + host.scrollTop;
    host.scrollTo({ top: Math.max(0, top - 8), behavior: 'smooth' });
  }

  /** Opens the assistant panel that lives in the shell. The manual stays on screen behind it. */
  askAssistant(): void {
    void this.assistant.open();
  }

  /**
   * The printable export.
   *
   * ★ FETCHED ONLY WHEN ASKED. The screen no longer needs the PDF to show the manual, so downloading
   * ~700 KB on every visit to power a button most readers never press would be pure waste. The blob is
   * still authenticated, and it still never becomes a public URL.
   */
  openPdf(): void {
    if (this.pdfUrl) {
      window.open(this.pdfUrl, '_blank', 'noopener');
      return;
    }

    this.api.getPdf().subscribe({
      next: blob => {
        this.pdfUrl = URL.createObjectURL(blob);
        window.open(this.pdfUrl, '_blank', 'noopener');
      },
      // Silent by design: the manual is already on screen. A toast about the optional export would be
      // noise about something the reader does not need.
      error: () => undefined,
    });
  }

  private release(): void {
    if (this.pdfUrl) {
      URL.revokeObjectURL(this.pdfUrl);
      this.pdfUrl = null;
    }
    this.html.set('');
    this.headings.set([]);
  }
}
