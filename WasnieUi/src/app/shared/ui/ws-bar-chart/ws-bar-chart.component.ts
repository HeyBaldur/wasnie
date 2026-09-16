import {
  afterNextRender,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  OnDestroy,
  ViewChild,
} from '@angular/core';
import {
  ActiveElement,
  BarController,
  BarElement,
  CategoryScale,
  Chart,
  ChartArea,
  LinearScale,
  Plugin,
  ScriptableContext,
  Tooltip,
} from 'chart.js';

import {
  ChartPalette,
  chartTooltip,
  prefersReducedMotion,
  readChartPalette,
  replayWhenVisible,
  withAlpha,
} from '../chart-theme';

Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip);

export interface BarChartPoint {
  label: string;
  value: number;
  currency?: string;
  isCurrent?: boolean;
}

/**
 * Vertical bar chart for monthly earnings trends (the payee's "Sales trend", 12 months).
 *
 * ★ TOKENS, READ AT BUILD TIME — see chart-theme.ts (shared with ws-hbar-chart and ws-sparkline-chart).
 *
 * ★ THE CURRENT MONTH IS THE ONE THAT SHINES. Past months are a softer gradient; the current one is solid, glows
 * and carries its value in a pill above it. Hovering any month lights its column and shows the exact amount.
 *
 * ★ THE ENTRANCE PLAYS WHEN IT IS SEEN. The chart often lives in a collapsed card, so its first render happens
 * off screen; an IntersectionObserver replays the grow-in the first time the canvas actually becomes visible.
 * With reduced motion there is no animation at all.
 */
@Component({
  selector: 'ws-bar-chart',
  standalone: true,
  templateUrl: './ws-bar-chart.component.html',
  styleUrl: './ws-bar-chart.component.scss',
})
export class WsBarChartComponent implements OnDestroy {
  @ViewChild('canvas', { static: true }) private canvasRef!: ElementRef<HTMLCanvasElement>;
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly points    = input<BarChartPoint[]>([]);
  readonly emptyLabel = input('');

  readonly hasData = computed(() => this.points().some(p => p.value > 0));

  private chart: Chart<'bar', number[], string> | null = null;
  private visibility: IntersectionObserver | null = null;

  constructor() {
    afterNextRender(() => { if (!this.chart) this.initChart(); });

    effect(() => {
      const pts = this.points();
      if (!pts.some(p => p.value > 0)) return;

      if (!this.chart) {
        // Data arrived after initial render — canvas exists, chart not yet created
        this.initChart();
        return;
      }

      this.chart.data.labels = pts.map(p => p.label);
      this.chart.data.datasets[0].data = pts.map(p => p.value);
      this.chart.update('none');
    });
  }

  private initChart(): void {
    const canvas = this.canvasRef.nativeElement;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    if (!this.points().some(p => p.value > 0)) return;

    const palette = readChartPalette(this.host.nativeElement, ctx);
    const reducedMotion = prefersReducedMotion();
    const currency = () => this.points().find(p => p.currency)?.currency ?? '';
    const isCurrent = (i: number) => this.points()[i]?.isCurrent === true;

    const barFill = (context: ScriptableContext<'bar'>, hover: boolean): CanvasGradient | string => {
      const area = context.chart.chartArea as ChartArea | undefined;
      if (!area) return palette.blue;
      const g = context.chart.ctx.createLinearGradient(0, area.bottom, 0, area.top);
      if (isCurrent(context.dataIndex)) {
        g.addColorStop(0, palette.blue);
        g.addColorStop(1, palette.violet);
      } else {
        g.addColorStop(0, withAlpha(palette.blue, hover ? 0.55 : 0.28));
        g.addColorStop(1, withAlpha(palette.violet, hover ? 0.85 : 0.5));
      }
      return g;
    };

    this.chart = new Chart<'bar', number[], string>(canvas, {
      type: 'bar',
      data: {
        labels: this.points().map(p => p.label),
        datasets: [{
          data: this.points().map(p => p.value),
          backgroundColor: (c) => barFill(c, false),
          hoverBackgroundColor: (c) => barFill(c, true),
          borderWidth: 0,
          borderRadius: { topLeft: 8, topRight: 8, bottomLeft: 3, bottomRight: 3 },
          borderSkipped: false,
          minBarLength: 6,
          barPercentage: 0.66,
          categoryPercentage: 0.9,
        }],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        layout: { padding: { top: 28 } },
        interaction: { mode: 'index', intersect: false },
        animation: reducedMotion
          ? false
          : {
              duration: 900,
              easing: 'easeOutQuart',
              delay: (c) => (c.type === 'data' && c.mode === 'default' ? c.dataIndex * 45 : 0),
            },
        plugins: {
          legend: { display: false },
          tooltip: {
            ...chartTooltip(palette),
            callbacks: {
              label: (tooltipCtx) => this.fmt(tooltipCtx.parsed.y ?? 0, currency()),
            },
          },
        },
        scales: {
          x: {
            grid: { display: false },
            border: { display: false },
            ticks: {
              color: (c) => (isCurrent(c.index) ? palette.violet : palette.tick),
              font: (c) => ({ size: 11, weight: isCurrent(c.index) ? 700 : 500 }),
              maxRotation: 0,
            },
          },
          y: {
            grid: { color: palette.grid, tickBorderDash: [4, 4] },
            border: { display: false, dash: [4, 4] },
            ticks: {
              color: palette.tick,
              font: { size: 11 },
              maxTicksLimit: 5,
              padding: 8,
              callback: (val) => this.fmtAxis(Number(val)),
            },
          },
        },
      },
      plugins: [this.hoverColumn(palette), this.currentGlow(palette), this.currentLabel(palette, currency)],
    });

    this.visibility?.disconnect();
    this.visibility = replayWhenVisible(canvas, () => {
      this.chart?.reset();
      this.chart?.update();
    });
  }

  // ── Plugins ────────────────────────────────────────────────────────────────

  /** A soft column behind the hovered month. */
  private hoverColumn(palette: ChartPalette): Plugin<'bar'> {
    return {
      id: 'wsHoverColumn',
      beforeDatasetsDraw: (chart) => {
        const active: ActiveElement[] = chart.getActiveElements();
        if (!active.length) return;
        const bar = chart.getDatasetMeta(0).data[active[0].index] as unknown as { x: number; width: number };
        const { top, bottom } = chart.chartArea;
        const width = bar.width * 1.7;
        const c = chart.ctx;
        c.save();
        c.fillStyle = withAlpha(palette.violet, 0.08);
        c.beginPath();
        c.roundRect(bar.x - width / 2, top, width, bottom - top, 8);
        c.fill();
        c.restore();
      },
    };
  }

  /** The current month's bar glows. */
  private currentGlow(palette: ChartPalette): Plugin<'bar'> {
    return {
      id: 'wsCurrentGlow',
      beforeDatasetDraw: (chart) => {
        chart.ctx.save();
        chart.ctx.shadowColor = withAlpha(palette.violet, 0.45);
        chart.ctx.shadowBlur = 0;
      },
      afterDatasetDraw: (chart) => {
        chart.ctx.restore();
        const index = this.points().findIndex(p => p.isCurrent);
        if (index < 0) return;
        const bar = chart.getDatasetMeta(0).data[index] as unknown as { draw: (ctx: CanvasRenderingContext2D) => void };
        if (!bar) return;
        const c = chart.ctx;
        c.save();
        c.shadowColor = withAlpha(palette.violet, 0.5);
        c.shadowBlur = 16;
        c.shadowOffsetY = 4;
        bar.draw(c);
        c.restore();
      },
    };
  }

  /** The current month's value, in a pill above its bar. */
  private currentLabel(palette: ChartPalette, currency: () => string): Plugin<'bar'> {
    return {
      id: 'wsCurrentLabel',
      afterDatasetsDraw: (chart) => {
        const index = this.points().findIndex(p => p.isCurrent);
        if (index < 0) return;
        const value = this.points()[index].value;
        if (!(value > 0)) return;
        const bar = chart.getDatasetMeta(0).data[index] as unknown as { x: number; y: number };
        if (!bar) return;

        const c = chart.ctx;
        const text = this.fmt(value, currency());
        c.save();
        c.font = '700 11px system-ui, -apple-system, "Segoe UI", sans-serif';
        const w = c.measureText(text).width + 14;
        const h = 20;
        const x = Math.min(Math.max(bar.x - w / 2, chart.chartArea.left), chart.chartArea.right - w);
        const y = Math.max(bar.y - h - 8, 2);
        c.fillStyle = palette.violet;
        c.beginPath();
        c.roundRect(x, y, w, h, 10);
        c.fill();
        c.fillStyle = palette.inverse;
        c.textAlign = 'center';
        c.textBaseline = 'middle';
        c.fillText(text, x + w / 2, y + h / 2 + 0.5);
        c.restore();
      },
    };
  }

  ngOnDestroy(): void {
    this.visibility?.disconnect();
    this.chart?.destroy();
  }

  // ── Formatting ─────────────────────────────────────────────────────────────

  private fmtAxis(value: number): string {
    const abs = Math.abs(value);
    if (abs >= 1e12) return `${(value / 1e12).toFixed(1)}T`;
    if (abs >= 1e9)  return `${(value / 1e9).toFixed(1)}B`;
    if (abs >= 1e6)  return `${(value / 1e6).toFixed(1)}M`;
    if (abs >= 1e3)  return `${(value / 1e3).toFixed(1)}K`;
    return value.toFixed(0);
  }

  private fmt(value: number, currency: string): string {
    return currency
      ? new Intl.NumberFormat('en-US', {
          style: 'currency', currency,
          notation: 'compact', maximumFractionDigits: 2,
        }).format(value)
      : value.toLocaleString('en-US', { maximumFractionDigits: 0 });
  }

  /** Public for tests and backward compatibility. */
  formatValue(value: number, currency: string): string {
    return this.fmt(value, currency);
  }
}
