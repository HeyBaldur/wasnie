import {
  Directive,
  ElementRef,
  HostListener,
  OnDestroy,
  inject,
  input,
} from '@angular/core';

@Directive({
  selector: '[wsTooltip]',
  standalone: true,
})
export class WsTooltipDirective implements OnDestroy {
  private readonly el = inject(ElementRef);
  private tooltipEl: HTMLElement | null = null;
  private showTimer: ReturnType<typeof setTimeout> | null = null;

  readonly wsTooltip = input('');
  readonly tooltipPlacement = input<'top' | 'bottom' | 'left' | 'right'>('top');

  @HostListener('mouseenter')
  @HostListener('focusin')
  onShow(): void {
    this.showTimer = setTimeout(() => this.createTooltip(), 300);
  }

  @HostListener('mouseleave')
  @HostListener('focusout')
  onHide(): void {
    if (this.showTimer) {
      clearTimeout(this.showTimer);
      this.showTimer = null;
    }
    this.removeTooltip();
  }

  ngOnDestroy(): void {
    if (this.showTimer) clearTimeout(this.showTimer);
    this.removeTooltip();
  }

  private createTooltip(): void {
    const text = this.wsTooltip();
    if (!text) return;

    this.removeTooltip();

    const tip = document.createElement('div');
    tip.className = 'ws-tooltip';
    tip.textContent = text;
    tip.setAttribute('role', 'tooltip');
    document.body.appendChild(tip);
    this.tooltipEl = tip;

    this.position(tip);
  }

  private position(tip: HTMLElement): void {
    const hostRect = this.el.nativeElement.getBoundingClientRect();
    const placement = this.tooltipPlacement();

    tip.style.position = 'fixed';
    tip.style.zIndex = '9999';

    requestAnimationFrame(() => {
      const tipRect = tip.getBoundingClientRect();
      const gap = 6;

      let top = 0;
      let left = 0;

      if (placement === 'top') {
        top = hostRect.top - tipRect.height - gap;
        left = hostRect.left + hostRect.width / 2 - tipRect.width / 2;
      } else if (placement === 'bottom') {
        top = hostRect.bottom + gap;
        left = hostRect.left + hostRect.width / 2 - tipRect.width / 2;
      } else if (placement === 'left') {
        top = hostRect.top + hostRect.height / 2 - tipRect.height / 2;
        left = hostRect.left - tipRect.width - gap;
      } else {
        top = hostRect.top + hostRect.height / 2 - tipRect.height / 2;
        left = hostRect.right + gap;
      }

      tip.style.top = `${Math.max(4, top)}px`;
      tip.style.left = `${Math.max(4, left)}px`;
    });
  }

  private removeTooltip(): void {
    if (this.tooltipEl) {
      this.tooltipEl.remove();
      this.tooltipEl = null;
    }
  }
}
