import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  effect,
  input,
  model,
  viewChild,
  viewChildren,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

export interface SegOption {
  label: string;
  value: string;
}

@Component({
  selector: 'ws-segmented-control',
  standalone: true,
  imports: [TranslatePipe],
  templateUrl: './ws-segmented-control.component.html',
  styleUrl: './ws-segmented-control.component.scss',
})
export class WsSegmentedControlComponent implements AfterViewInit, OnDestroy {
  readonly options = input.required<SegOption[]>();
  readonly value = model<string>('');
  readonly translateLabels = input(true);

  private readonly indicatorEl = viewChild<ElementRef<HTMLSpanElement>>('indicator');
  private readonly segmentsRef = viewChildren<ElementRef<HTMLButtonElement>>('segment');

  private resizeObserver?: ResizeObserver;

  constructor() {
    effect(() => {
      this.value();
      queueMicrotask(() => this.updateIndicator());
    });
  }

  ngAfterViewInit(): void {
    this.updateIndicator();
    this.resizeObserver = new ResizeObserver(() => this.updateIndicator());
    this.segmentsRef().forEach(s => this.resizeObserver?.observe(s.nativeElement));
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
  }

  select(value: string): void {
    this.value.set(value);
    queueMicrotask(() => this.updateIndicator());
  }

  onKeydown(event: KeyboardEvent, value: string): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.select(value);
    }
  }

  private updateIndicator(): void {
    const selectedIndex = this.options().findIndex(o => o.value === this.value());
    const idx = selectedIndex >= 0 ? selectedIndex : 0;
    const segs = this.segmentsRef();
    const indicator = this.indicatorEl();
    if (!segs[idx] || !indicator) return;
    const el = segs[idx].nativeElement;
    indicator.nativeElement.style.width = `${el.offsetWidth}px`;
    indicator.nativeElement.style.transform = `translateX(${el.offsetLeft}px)`;
  }
}
