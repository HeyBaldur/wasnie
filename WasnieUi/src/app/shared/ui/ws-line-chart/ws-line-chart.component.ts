import { Component, computed, ElementRef, HostListener, input, signal, ViewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';

export interface LineChartPoint {
  label: string;   // e.g. "Jan 2026"
  value: number;
  currency?: string;
}

export interface LineChartSeries {
  currency: string;
  points: LineChartPoint[];
  color: string;
}

// Accent colors for up to 3 currency lines
const SERIES_COLORS = [
  'var(--color-brand)',
  'var(--color-success)',
  'var(--color-warning)',
];

@Component({
  selector: 'ws-line-chart',
  standalone: true,
  imports: [DecimalPipe],
  templateUrl: './ws-line-chart.component.html',
  styleUrl: './ws-line-chart.component.scss',
})
export class WsLineChartComponent {
  readonly points = input<LineChartPoint[]>([]);
  readonly emptyLabel = input('');

  // Chart drawing constants
  readonly VB_W = 560;
  readonly VB_H = 180;
  readonly ML = 52;  // margin left (Y labels)
  readonly MR = 16;  // margin right
  readonly MT = 16;  // margin top
  readonly MB = 32;  // margin bottom (X labels)

  readonly chartW = computed(() => this.VB_W - this.ML - this.MR);
  readonly chartH = computed(() => this.VB_H - this.MT - this.MB);

  readonly series = computed<LineChartSeries[]>(() => {
    const pts = this.points();
    if (!pts.length) return [];

    // Group by currency
    const byCurrency = new Map<string, LineChartPoint[]>();
    for (const p of pts) {
      const cur = p.currency ?? '';
      if (!byCurrency.has(cur)) byCurrency.set(cur, []);
      byCurrency.get(cur)!.push(p);
    }

    return Array.from(byCurrency.entries())
      .slice(0, 3)
      .map(([currency, sp], i) => ({
        currency,
        points: sp,
        color: SERIES_COLORS[i],
      }));
  });

  readonly allLabels = computed<string[]>(() => {
    const set = new Set<string>();
    for (const p of this.points()) set.add(p.label);
    return Array.from(set);
  });

  readonly maxValue = computed(() => {
    const vals = this.points().map(p => p.value);
    if (!vals.length) return 1;
    const m = Math.max(...vals);
    // Round up to nice number
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

  xToSvg(index: number, total: number): number {
    if (total <= 1) return this.ML + this.chartW() / 2;
    return this.ML + (index / (total - 1)) * this.chartW();
  }

  yToSvg(value: number): number {
    return this.MT + this.chartH() - (value / this.maxValue()) * this.chartH();
  }

  polylinePoints(s: LineChartSeries): string {
    const labels = this.allLabels();
    const total = labels.length;
    return s.points
      .map(p => {
        const idx = labels.indexOf(p.label);
        const x = this.xToSvg(idx, total);
        const y = this.yToSvg(p.value);
        return `${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(' ');
  }

  areaPath(s: LineChartSeries): string {
    const labels = this.allLabels();
    const total = labels.length;
    if (!s.points.length) return '';
    const linePoints = s.points.map(p => {
      const idx = labels.indexOf(p.label);
      return `${this.xToSvg(idx, total).toFixed(1)},${this.yToSvg(p.value).toFixed(1)}`;
    });
    const firstX = this.xToSvg(labels.indexOf(s.points[0].label), total).toFixed(1);
    const lastX = this.xToSvg(labels.indexOf(s.points[s.points.length - 1].label), total).toFixed(1);
    const bottom = (this.MT + this.chartH()).toFixed(1);
    return `M ${firstX},${bottom} L ${linePoints.join(' L ')} L ${lastX},${bottom} Z`;
  }

  // ── Tooltip ───────────────────────────────────────────────────────────────

  readonly tooltipVisible = signal(false);
  readonly tooltipX = signal(0);
  readonly tooltipY = signal(0);
  readonly tooltipItems = signal<{ label: string; value: number; currency: string; color: string }[]>([]);
  readonly tooltipLabel = signal('');

  @ViewChild('svgEl') svgEl?: ElementRef<SVGSVGElement>;

  onSvgMouseMove(event: MouseEvent): void {
    const svg = this.svgEl?.nativeElement;
    if (!svg) return;
    const rect = svg.getBoundingClientRect();
    const svgX = ((event.clientX - rect.left) / rect.width) * this.VB_W;

    const labels = this.allLabels();
    if (!labels.length) return;
    const total = labels.length;

    // Find nearest label index
    const nearest = labels.reduce((best, _lbl, i) => {
      const x = this.xToSvg(i, total);
      return Math.abs(svgX - x) < Math.abs(svgX - this.xToSvg(best, total)) ? i : best;
    }, 0);

    const label = labels[nearest];
    const items = this.series()
      .map(s => {
        const pt = s.points.find(p => p.label === label);
        return pt ? { label, value: pt.value, currency: s.currency, color: s.color } : null;
      })
      .filter((x): x is NonNullable<typeof x> => x !== null);

    if (!items.length) return;

    const nearX = this.xToSvg(nearest, total);
    // Clamp to [8, 75]% — prevents right-edge overflow. Below 8% anchors at left edge.
    const rawPct = (nearX / this.VB_W) * 100;
    this.tooltipX.set(Math.min(Math.max(rawPct, 8), 75));
    this.tooltipY.set(20); // fixed top % inside card
    this.tooltipLabel.set(label);
    this.tooltipItems.set(items);
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

  gradientId(s: LineChartSeries): string {
    return `area-grad-${s.currency || 'default'}`;
  }
}
