import {
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';

type Placement = 'bottom' | 'top' | 'bottom-end' | 'top-end';

/** How close to the edge the panel may sit before it is considered not to fit. */
const EDGE_MARGIN_PX = 8;

@Component({
  selector: 'ws-popover',
  standalone: true,
  templateUrl: './ws-popover.component.html',
  styleUrl: './ws-popover.component.scss',
})
export class WsPopoverComponent {
  private readonly panelRef = viewChild<ElementRef<HTMLElement>>('popover');

  private readonly host = inject(ElementRef);

  readonly isOpen = model(false);

  /**
   * The PREFERRED placement. It is not a promise: when the panel would not fit on that side it is
   * flipped to the opposite one — see {@link resolvedPlacement}.
   */
  readonly placement = input<Placement>('bottom');

  readonly openedChange = output<boolean>();

  /**
   * ★★ THE SIDE THE PANEL ACTUALLY OPENS ON, WHICH IS NOT ALWAYS THE ONE THAT WAS ASKED FOR.
   *
   * The conversation rail is a scroll container (`overflow-y: auto`), and the menu is positioned
   * inside it. For the last conversations in a long list, a `bottom` panel opened straight past the
   * bottom edge: the options were there, laid out, and invisible — the only way to reach Delete was
   * to scroll the rail down first, which is not something a menu should ever ask.
   *
   * ★ IT MEASURES, IT DOES NOT GUESS AT A ROW COUNT. Whether a panel fits depends on how tall THIS
   * panel is and how much room is left under THIS trigger, and both change with the menu's contents
   * and the window. A rule like "flip for the last three rows" would be right for one list at one
   * height.
   */
  private readonly flipped = signal(false);

  readonly resolvedPlacement = computed<Placement>(() => {
    const preferred = this.placement();
    if (!this.flipped()) return preferred;

    return preferred === 'bottom' ? 'top'
      : preferred === 'bottom-end' ? 'top-end'
      : preferred === 'top' ? 'bottom'
      : 'bottom-end';
  });

  constructor() {
    // The signal-based viewChild resolves once the @if has put the panel in the DOM, so this runs
    // with something real to measure — and before the browser paints, so the panel does not appear
    // on the wrong side and jump.
    effect(() => {
      const panel = this.panelRef()?.nativeElement;
      if (!panel) {
        this.flipped.set(false);
        return;
      }
      this.applyFlip(panel);
    });

    // ★ CAPTURE, AND THEREFORE NOT @HostListener. The room under the trigger changes while the panel
    // is open — the rail scrolls, the window resizes — but a scroll event does NOT bubble, so
    // `@HostListener('document:scroll')` would never hear the rail move and the menu would keep the
    // side it was given on open. Capture phase is the only way to see it from the document.
    const onViewportChange = () => {
      const panel = this.panelRef()?.nativeElement;
      if (panel) this.applyFlip(panel);
    };

    document.addEventListener('scroll', onViewportChange, true);
    window.addEventListener('resize', onViewportChange);

    inject(DestroyRef).onDestroy(() => {
      document.removeEventListener('scroll', onViewportChange, true);
      window.removeEventListener('resize', onViewportChange);
    });
  }

  toggle(): void {
    this.isOpen.update(v => !v);
    this.openedChange.emit(this.isOpen());
  }

  open(): void {
    this.isOpen.set(true);
    this.openedChange.emit(true);
  }

  close(): void {
    this.isOpen.set(false);
    this.openedChange.emit(false);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event): void {
    if (!this.isOpen()) return;
    if (!this.host.nativeElement.contains(event.target)) {
      this.close();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.isOpen()) this.close();
  }

  stopProp(event: Event): void {
    event.stopPropagation();
  }

  /**
   * Decides the side by comparing the panel's height against the room above and below the trigger,
   * bounded by the nearest scrolling ancestor (the rail) rather than only the viewport — clipping
   * happens at the scroll container's edge, which is usually well inside the window.
   *
   * ★ FLIPPING IS A LAST RESORT, NOT A PREFERENCE. It only happens when the preferred side does not
   * fit AND the opposite one does. If neither fits the panel stays where the caller asked: moving it
   * would trade one clipped edge for another while also surprising the user about where their menu
   * appears.
   */
  private applyFlip(panel: HTMLElement): void {
    const trigger = this.host.nativeElement.getBoundingClientRect() as DOMRect;
    const panelHeight = panel.offsetHeight;
    const bounds = this.clippingBounds();

    const roomBelow = bounds.bottom - trigger.bottom - EDGE_MARGIN_PX;
    const roomAbove = trigger.top - bounds.top - EDGE_MARGIN_PX;

    const opensDown = this.placement() === 'bottom' || this.placement() === 'bottom-end';
    const roomPreferred = opensDown ? roomBelow : roomAbove;
    const roomOpposite = opensDown ? roomAbove : roomBelow;

    this.flipped.set(panelHeight > roomPreferred && panelHeight <= roomOpposite);
  }

  /**
   * The edges the panel would actually be clipped by: the nearest ancestor that scrolls or hides its
   * overflow, intersected with the viewport. Walking up rather than assuming the window is what makes
   * this work inside the assistant rail and inside the drawer, which clip long before the screen does.
   */
  private clippingBounds(): { top: number; bottom: number } {
    let top = 0;
    let bottom = window.innerHeight;

    let el = this.host.nativeElement.parentElement as HTMLElement | null;
    while (el && el !== document.body) {
      const overflowY = getComputedStyle(el).overflowY;
      if (overflowY === 'auto' || overflowY === 'scroll' || overflowY === 'hidden') {
        const rect = el.getBoundingClientRect();
        top = Math.max(top, rect.top);
        bottom = Math.min(bottom, rect.bottom);
        break;
      }
      el = el.parentElement;
    }

    return { top, bottom };
  }
}
