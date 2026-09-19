import { DestroyRef, Directive, ElementRef, NgZone, afterNextRender, inject, input } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

/** A column can never be dragged narrower than this — below it even an ellipsis stops being legible. */
export const MIN_COLUMN_WIDTH = 56;
/** Nor wider. A runaway drag should not produce a column wider than most screens. */
export const MAX_COLUMN_WIDTH = 960;
/** Double-click fits the content, up to here: a 300-character deal name is still read on hover. */
export const MAX_FIT_WIDTH = 640;
/** One arrow-key press on a focused handle. */
const KEY_STEP = 16;

/**
 * Spreadsheet-style column widths for a `<ws-table>`: drag a header's right edge to resize, double-
 * click it to fit the content, and the widths are remembered per table in this browser.
 *
 * ```html
 * <ws-table>
 *   <table wsResizableColumns="transactions">
 *     <thead><tr>
 *       <th data-col="reference" data-width="260">…</th>   ← resizable, 260px until moved
 *       <th>…</th>                                          ← no data-col: fixed at its natural width
 *       <th></th>                                           ← the LAST column fills what is left
 * ```
 *
 * ★★ WHY THIS AND NOT AG GRID (decided 2026-09-19). Resizing was the one grid feature the screens
 * needed, and AG Grid would have brought a second way of building tables that has to be themed by
 * hand to look like this one — every cell (badges, links, copy buttons, row checkboxes) rewritten as
 * a renderer. This keeps the table the table: the directive only owns widths.
 *
 * ★★ FIXED LAYOUT IS WHAT MAKES THE WIDTH A DECISION. With the browser's automatic layout a long
 * reference number widens its own column and pushes the copy button onto a line of its own, which is
 * the defect that started this. Under `table-layout: fixed` the header widths are the widths, and a
 * cell that does not fit truncates with an ellipsis — the reader widens the column to see more, as in
 * Excel, instead of the data rearranging the page.
 *
 * ★ THE LAST COLUMN IS THE FILLER. It takes whatever the others leave of the container (never less
 * than its own content), so shrinking a column does not leave a strip of bare table on the right and
 * widening one scrolls sideways instead of squeezing its neighbours. It is not resizable itself.
 *
 * ★ FITTING MEASURES THE REAL CONTENT. For a moment the table goes back to automatic layout with
 * nothing truncated (`ws-table--measuring`), the header reports its natural width, and the fixed
 * widths come back — all inside one task, so nothing is painted in between.
 *
 * ★ WIDTHS ARE A PER-VIEWER CONVENIENCE, so they live in localStorage and every access is guarded: a
 * private window or blocked storage simply starts from the defaults.
 */
@Directive({
  selector: 'table[wsResizableColumns]',
  standalone: true,
  host: { class: 'ws-table--resizable' },
})
export class WsResizableColumnsDirective {
  /** The key the widths are remembered under. One per table — two tables must not share one. */
  readonly wsResizableColumns = input.required<string>();

  private readonly table = inject<ElementRef<HTMLTableElement>>(ElementRef).nativeElement;
  private readonly zone = inject(NgZone);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  private headers: HTMLTableCellElement[] = [];
  private widths: number[] = [];
  private fillerMin = 0;

  constructor() {
    afterNextRender(() => this.zone.runOutsideAngular(() => this.setup()));
  }

  // ── Setup ────────────────────────────────────────────────────────────────────────────────────

  private setup(): void {
    const row = this.table.tHead?.rows[this.table.tHead.rows.length - 1];
    if (!row) return;

    this.headers = Array.from(row.cells) as HTMLTableCellElement[];
    const natural = this.measureNatural();
    const stored = this.load();
    const last = this.headers.length - 1;

    this.widths = this.headers.map((th, i) => {
      if (i === last) return natural[i];
      const key = th.dataset['col'];
      if (!key) return natural[i];
      const remembered = stored[key];
      if (typeof remembered === 'number') return clamp(remembered);
      const declared = Number(th.dataset['width']);
      return clamp(Number.isFinite(declared) && declared > 0 ? declared : natural[i]);
    });
    this.fillerMin = natural[last];

    this.headers.forEach((th, i) => {
      if (i !== last && th.dataset['col']) this.addHandle(th, i);
    });

    this.apply();

    const wrap = this.table.parentElement;
    if (wrap && typeof ResizeObserver !== 'undefined') {
      const observer = new ResizeObserver(() => this.apply());
      observer.observe(wrap);
      this.destroyRef.onDestroy(() => observer.disconnect());
    }

    // ★ THE FIRST RENDER IS USUALLY THE SKELETON. Measured then, the filler's minimum would be an empty
    // header's width and the real row buttons would be clipped once the data arrived — so the filler
    // is re-measured whenever the rows change. The user's widths are not touched.
    if (typeof MutationObserver !== 'undefined') {
      let queued = false;
      const rows = new MutationObserver(() => {
        if (queued) return;
        queued = true;
        requestAnimationFrame(() => {
          queued = false;
          this.fillerMin = this.measureNatural()[this.headers.length - 1];
          this.apply();
        });
      });
      Array.from(this.table.tBodies).forEach(tb => rows.observe(tb, { childList: true, subtree: true }));
      this.destroyRef.onDestroy(() => rows.disconnect());
    }
  }

  /** Every header's width under automatic layout with nothing truncated. */
  private measureNatural(): number[] {
    const saved = this.headers.map(th => th.style.width);
    const savedTable = this.table.style.width;

    this.headers.forEach(th => (th.style.width = ''));
    this.table.style.width = '';
    this.table.classList.add('ws-table--measuring');

    const widths = this.headers.map(th => Math.ceil(th.getBoundingClientRect().width));

    this.table.classList.remove('ws-table--measuring');
    this.headers.forEach((th, i) => (th.style.width = saved[i]));
    this.table.style.width = savedTable;

    return widths;
  }

  /** Writes the widths onto the headers; the filler takes what is left of the container. */
  private apply(): void {
    const last = this.headers.length - 1;
    if (last < 0) return;

    const others = this.widths.slice(0, last).reduce((sum, w) => sum + w, 0);
    const available = this.table.parentElement?.clientWidth ?? 0;
    this.widths[last] = Math.max(this.fillerMin, available - others);

    this.headers.forEach((th, i) => (th.style.width = `${this.widths[i]}px`));
    this.table.style.width = `${others + this.widths[last]}px`;
  }

  // ── The handle ───────────────────────────────────────────────────────────────────────────────

  private addHandle(th: HTMLTableCellElement, index: number): void {
    const handle = document.createElement('span');
    handle.className = 'ws-col-resizer';
    handle.tabIndex = 0;
    handle.setAttribute('role', 'separator');
    handle.setAttribute('aria-orientation', 'vertical');
    handle.setAttribute('aria-valuemin', String(MIN_COLUMN_WIDTH));
    handle.setAttribute('aria-valuemax', String(MAX_COLUMN_WIDTH));
    handle.setAttribute('aria-label',
      this.translate.instant('COMMON.RESIZE_COLUMN', { name: (th.textContent ?? '').trim() }));
    th.appendChild(handle);

    const onPointerDown = (e: PointerEvent) => {
      if (e.button !== 0) return;
      e.preventDefault();
      e.stopPropagation();

      const startX = e.clientX;
      const startWidth = this.widths[index];
      try {
        handle.setPointerCapture(e.pointerId);
      } catch {
        // The pointer is already gone (or the event was synthetic): the drag still works while the
        // pointer stays over the handle, which is the worst case, not a failure.
      }
      handle.classList.add('ws-col-resizer--active');
      document.body.classList.add('ws-col-resizing');

      const onMove = (ev: PointerEvent) => this.resize(index, startWidth + ev.clientX - startX, handle);
      const onUp = () => {
        handle.removeEventListener('pointermove', onMove);
        handle.removeEventListener('pointerup', onUp);
        handle.removeEventListener('pointercancel', onUp);
        handle.classList.remove('ws-col-resizer--active');
        document.body.classList.remove('ws-col-resizing');
        this.save();
      };

      handle.addEventListener('pointermove', onMove);
      handle.addEventListener('pointerup', onUp);
      handle.addEventListener('pointercancel', onUp);
    };

    const onDoubleClick = (e: MouseEvent) => {
      e.preventDefault();
      e.stopPropagation();
      this.fit(index, handle);
    };

    const onKeyDown = (e: KeyboardEvent) => {
      const step = e.key === 'ArrowRight' ? KEY_STEP : e.key === 'ArrowLeft' ? -KEY_STEP : 0;
      if (step) {
        e.preventDefault();
        this.resize(index, this.widths[index] + step, handle);
        this.save();
      } else if (e.key === 'Enter') {
        e.preventDefault();
        this.fit(index, handle);
      }
    };

    // A click on the edge must not also sort or navigate whatever the header does on click.
    const swallow = (e: Event) => e.stopPropagation();

    handle.addEventListener('pointerdown', onPointerDown);
    handle.addEventListener('dblclick', onDoubleClick);
    handle.addEventListener('keydown', onKeyDown);
    handle.addEventListener('click', swallow);
    handle.setAttribute('aria-valuenow', String(this.widths[index] ?? 0));
  }

  private resize(index: number, width: number, handle: HTMLElement): void {
    this.widths[index] = clamp(width);
    handle.setAttribute('aria-valuenow', String(this.widths[index]));
    this.apply();
  }

  /** Double-click: the column's natural width, as Excel's "AutoFit". */
  private fit(index: number, handle: HTMLElement): void {
    const natural = this.measureNatural()[index];
    this.resize(index, Math.min(natural, MAX_FIT_WIDTH), handle);
    this.save();
  }

  // ── Memory ───────────────────────────────────────────────────────────────────────────────────

  private storageKey(): string {
    return `ws-table:cols:${this.wsResizableColumns()}`;
  }

  private load(): Record<string, number> {
    try {
      const raw = localStorage.getItem(this.storageKey());
      const parsed = raw ? JSON.parse(raw) : null;
      return parsed && typeof parsed === 'object' ? parsed : {};
    } catch {
      return {};
    }
  }

  private save(): void {
    const byKey: Record<string, number> = {};
    this.headers.forEach((th, i) => {
      const key = th.dataset['col'];
      if (key && i < this.headers.length - 1) byKey[key] = this.widths[i];
    });
    try {
      localStorage.setItem(this.storageKey(), JSON.stringify(byKey));
    } catch {
      // Storage unavailable: the widths last for this visit only.
    }
  }
}

function clamp(width: number): number {
  return Math.round(Math.min(MAX_COLUMN_WIDTH, Math.max(MIN_COLUMN_WIDTH, width)));
}
