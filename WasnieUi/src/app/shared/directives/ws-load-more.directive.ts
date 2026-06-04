import { Directive, ElementRef, inject, input, OnDestroy, OnInit, output } from '@angular/core';

/**
 * Observes the host element with IntersectionObserver.
 * When it enters the viewport, emits `wsLoadMore`.
 * Place on a sentinel <div> at the bottom of a list to trigger virtual scroll.
 */
@Directive({ selector: '[wsLoadMore]', standalone: true })
export class WsLoadMoreDirective implements OnInit, OnDestroy {
  private readonly el = inject(ElementRef<HTMLElement>);
  readonly wsLoadMore = output<void>();
  // Set to false while loading to prevent duplicate triggers
  readonly enabled = input(true);

  private _observer?: IntersectionObserver;

  ngOnInit(): void {
    this._observer = new IntersectionObserver(
      (entries) => {
        if (entries[0].isIntersecting && this.enabled()) {
          this.wsLoadMore.emit();
        }
      },
      { threshold: 0.1 }
    );
    this._observer.observe(this.el.nativeElement);
  }

  ngOnDestroy(): void {
    this._observer?.disconnect();
  }
}
