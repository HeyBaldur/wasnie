import { Component, computed, ElementRef, input, signal, ViewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';

export interface BarChartPoint {
  label: string;
  value: number;
  currency?: string;
  isCurrent?: boolean;
}

@Component({
  selector: 'ws-bar-chart',
  standalone: true,
  imports: [DecimalPipe],
  templateUrl: './ws-bar-chart.component.html',
  styleUrl: './ws-bar-chart.component.scss',
})
export class WsBarChartComponent {
  readonly points = input<BarChartPoint[]>([]);
  readonly emptyLabel = input('');

  // SVG drawing constants — match ws-line-chart for consistent card appearance
  readonly VB_W = 560;
  readonly VB_H = 180;
  readonly ML = 52;   // margin left (Y-axis labels)
  readonly MR = 16;   // margin right
  readonly MT = 16;   // margin top
  readonly MB = 32;   // margin bottom (X-axis labels)

  readonly chartW = computed(() => this.VB_W - this.ML - this.MR);
  readonly chartH = computed(() => this.VB_H - this.MT - this.MB);
  readonly bucketW = computed(() => this.chartW() / Math.max(1, this.points().length));
  readonly barW = computed(() => this.bucketW() * 0.7);

  // Show bars only if at least one month has a non-zero value
  readonly hasData = computed(() => this.points().some(p => p.value > 0));

  readonly maxValue = computed(() => {
    const vals = this.points().map(p => p.value);
    if (!vals.length || Math.max(...vals) === 0) return 1;
    const m = Math.max(...vals);
    const magnitude = Math.pow(10, Math.floor(Math.log10(m || 1)));
    return Math.ceil(m / magnitude) * magnitude || 1;
  });

  readonly yTicks = computed(() => {
    const max = this.maxValue();
    return [0, 0.25, 0.5, 0.75, 1.0].map(f => ({
      value: max * f,
      y: this.yToSvg(max * f),
    }));
  });

  readonly bars = computed(() => {
    const pts = this.points();
    const bW = this.bucketW();
    const barW = this.barW();
    const chartH = this.chartH();
    const max = this.maxValue();
    const MIN_H = 2; // 2 px stub for zero-value months so the slot is visible

    return pts.map((p, i) => {
      const barH = p.value > 0
        ? Math.max(MIN_H, (p.value / max) * chartH)
        : MIN_H;
      const x = this.ML + i * bW + (bW - barW) / 2;
      const y = this.MT + chartH - barH;
      return { x, y, width: barW, height: barH, point: p, index: i };
    });
  });

  barLabelX(i: number): number {
    return this.ML + i * this.bucketW() + this.bucketW() / 2;
  }

  yToSvg(value: number): number {
    return this.MT + this.chartH() - (value / this.maxValue()) * this.chartH();
  }

  // ── Tooltip ──────────────────────────────────────────────────────────────────

  readonly tooltipVisible = signal(false);
  readonly tooltipX = signal(0);
  readonly tooltipLabel = signal('');
  readonly tooltipValue = signal(0);
  readonly tooltipCurrency = signal('');
  readonly tooltipIsEmpty = signal(false);

  @ViewChild('svgEl') svgEl?: ElementRef<SVGSVGElement>;

  onSvgMouseMove(event: MouseEvent): void {
    const svg = this.svgEl?.nativeElement;
    if (!svg) return;
    const rect = svg.getBoundingClientRect();
    const svgX = ((event.clientX - rect.left) / rect.width) * this.VB_W;

    const pts = this.points();
    if (!pts.length) return;

    // Which bucket is the cursor in?
    const idx = Math.max(0, Math.min(pts.length - 1, Math.floor((svgX - this.ML) / this.bucketW())));
    const p = pts[idx];

    // Tooltip X: center of bucket, clamped [8%, 75%] to stay within card bounds
    const bucketCenterX = this.ML + idx * this.bucketW() + this.bucketW() / 2;
    const rawPct = (bucketCenterX / this.VB_W) * 100;
    this.tooltipX.set(Math.min(Math.max(rawPct, 8), 75));
    this.tooltipLabel.set(p.label);
    this.tooltipValue.set(p.value);
    this.tooltipCurrency.set(p.currency ?? '');
    this.tooltipIsEmpty.set(p.value === 0);
    this.tooltipVisible.set(true);
  }

  onSvgMouseLeave(): void {
    this.tooltipVisible.set(false);
  }

  formatValue(value: number, currency: string): string {
    if (currency) {
      return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency,
        minimumFractionDigits: 0,
        maximumFractionDigits: 0,
      }).format(value);
    }
    return value.toLocaleString('en-US', { maximumFractionDigits: 0 });
  }
}
