import {
  afterNextRender,
  Component,
  effect,
  ElementRef,
  inject,
  input,
  OnDestroy,
  ViewChild,
} from '@angular/core';
import {
  Chart,
  CategoryScale,
  ChartArea,
  Filler,
  LinearScale,
  LineController,
  LineElement,
  Plugin,
  PointElement,
  ScriptableContext,
  Tooltip,
} from 'chart.js';
import {
  ChartPalette,
  chartTooltip,
  normaliseColor,
  prefersReducedMotion,
  readChartPalette,
  replayWhenVisible,
  withAlpha,
} from '../chart-theme';

Chart.register(CategoryScale, LinearScale, LineController, LineElement, PointElement, Filler, Tooltip);

/**
 * Tiny Chart.js sparkline with hover tooltip.
 *
 * Pass `labels` (e.g. last-7-day date strings) so tooltip shows the day alongside the value. If labels is empty,
 * Chart.js uses point indices. `pointer-events-none` must NOT be on the host element — the parent div should
 * allow mouse events through.
 *
 * ★ SAME LANGUAGE AS THE OTHER CHARTS: a blue→violet stroke (or the `color` given) that glows, an area that fades
 * to nothing, the latest point marked with a haloed dot, a dashed guide under the pointer, and a line that rises
 * into place the first time it is seen. Colours come from the design tokens.
 */
@Component({
  selector: 'ws-sparkline-chart',
  standalone: true,
  template: `<canvas #canvas></canvas>`,
  styles: [`:host { display: block; width: 100%; height: 100%; }
            canvas { display: block; width: 100% !important; height: 100% !important; }`],
})
export class WsSparklineChartComponent implements OnDestroy {
  @ViewChild('canvas', { static: true }) private canvasRef!: ElementRef<HTMLCanvasElement>;
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly values   = input<number[]>([]);
  readonly labels   = input<string[]>([]);
  /** Optional single stroke colour (any CSS colour). Empty = the theme's blue→violet gradient. */
  readonly color    = input<string>('');
  readonly currency = input<string>('');

  private chart: Chart<'line', number[], string> | null = null;
  private visibility: IntersectionObserver | null = null;

  constructor() {
    afterNextRender(() => { if (!this.chart) this.initChart(); });

    effect(() => {
      const vals = this.values();
      const lbls = this.labels();
      if (this.chart) {
        this.chart.data.datasets[0].data = vals;
        this.chart.data.labels = lbls.length === vals.length ? lbls : vals.map((_, i) => String(i + 1));
        this.chart.update('none');
      }
    });
  }

  private initChart(): void {
    const canvas = this.canvasRef.nativeElement;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const palette = readChartPalette(this.host.nativeElement, ctx);
    const custom = this.color() ? normaliseColor(ctx, this.color()) : null;
    const start = custom ?? palette.blue;
    const end = custom ?? palette.violet;
    const reducedMotion = prefersReducedMotion();

    // Capture currency at init time so the tooltip closure is bound to THIS
    // instance's currency and cannot inherit a value from an adjacent sparkline.
    const currency = this.currency();
    const vals     = this.values();
    const lbls     = this.labels();
    const resolvedLabels = lbls.length === vals.length ? lbls : vals.map((_, i) => String(i + 1));

    const stroke = (c: ScriptableContext<'line'>): CanvasGradient | string => {
      const area = c.chart.chartArea as ChartArea | undefined;
      if (!area) return end;
      const g = c.chart.ctx.createLinearGradient(area.left, 0, area.right, 0);
      g.addColorStop(0, start);
      g.addColorStop(1, end);
      return g;
    };

    const fill = (c: ScriptableContext<'line'>): CanvasGradient | string => {
      const area = c.chart.chartArea as ChartArea | undefined;
      if (!area) return withAlpha(end, 0.15);
      const g = c.chart.ctx.createLinearGradient(0, area.top, 0, area.bottom);
      g.addColorStop(0, withAlpha(end, 0.3));
      g.addColorStop(1, withAlpha(start, 0));
      return g;
    };

    this.chart = new Chart(canvas, {
      type: 'line',
      data: {
        labels: resolvedLabels,
        datasets: [{
          data: vals,
          borderColor: stroke,
          borderWidth: 2,
          pointRadius: 0,
          pointHoverRadius: 4,
          pointHoverBorderWidth: 2,
          pointHoverBackgroundColor: palette.surface,
          pointHoverBorderColor: end,
          tension: 0.4,
          fill: true,
          backgroundColor: fill,
        }],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        layout: { padding: { top: 6, right: 6, bottom: 2, left: 2 } },
        animation: reducedMotion ? false : { duration: 800, easing: 'easeOutQuart' },
        animations: reducedMotion
          ? undefined
          : {
              y: {
                duration: 800,
                easing: 'easeOutQuart',
                from: (c: { chart: Chart }) => c.chart.chartArea?.bottom ?? c.chart.height,
                delay: (c: { type: string; dataIndex: number }) => (c.type === 'data' ? c.dataIndex * 30 : 0),
              },
            },
        interaction: {
          mode: 'index',
          intersect: false,
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            ...chartTooltip(palette),
            padding: { x: 10, y: 6 },
            callbacks: {
              label: (tooltipCtx) => {
                const val = tooltipCtx.parsed.y ?? 0;
                // Uses the closure-captured `currency` constant, not this.currency(),
                // to guarantee each sparkline instance formats its own data.
                if (currency) {
                  return new Intl.NumberFormat('en-US', {
                    style: 'currency', currency,
                    notation: 'compact', maximumFractionDigits: 2,
                  }).format(val);
                }
                return val.toLocaleString('en-US', { maximumFractionDigits: 0 });
              },
            },
          },
        },
        scales: {
          x: { display: false },
          y: { display: false },
        },
      },
      plugins: [this.glow(end), this.hoverGuide(palette), this.lastPoint(palette, end)],
    });

    this.visibility?.disconnect();
    this.visibility = replayWhenVisible(canvas, () => {
      this.chart?.reset();
      this.chart?.update();
    });
  }

  // ── Plugins ────────────────────────────────────────────────────────────────

  /** The line casts a soft glow of its own colour. */
  private glow(color: string): Plugin<'line'> {
    return {
      id: 'wsSparkGlow',
      beforeDatasetDraw: (chart) => {
        const c = chart.ctx;
        c.save();
        c.shadowColor = withAlpha(color, 0.45);
        c.shadowBlur = 8;
        c.shadowOffsetY = 3;
      },
      afterDatasetDraw: (chart) => {
        chart.ctx.restore();
      },
    };
  }

  /** A dashed vertical guide under the pointer. */
  private hoverGuide(palette: ChartPalette): Plugin<'line'> {
    return {
      id: 'wsSparkGuide',
      beforeDatasetsDraw: (chart) => {
        const active = chart.getActiveElements();
        if (!active.length) return;
        const { x } = active[0].element as unknown as { x: number };
        const { top, bottom } = chart.chartArea;
        const c = chart.ctx;
        c.save();
        c.strokeStyle = withAlpha(palette.tick, 0.6);
        c.setLineDash([3, 3]);
        c.lineWidth = 1;
        c.beginPath();
        c.moveTo(x, top);
        c.lineTo(x, bottom);
        c.stroke();
        c.restore();
      },
    };
  }

  /** The most recent point, marked with a haloed dot. */
  private lastPoint(palette: ChartPalette, color: string): Plugin<'line'> {
    return {
      id: 'wsSparkLast',
      afterDatasetsDraw: (chart) => {
        const points = chart.getDatasetMeta(0).data;
        const last = points[points.length - 1] as unknown as { x: number; y: number } | undefined;
        if (!last || chart.getActiveElements().length) return;
        const c = chart.ctx;
        c.save();
        c.fillStyle = withAlpha(color, 0.2);
        c.beginPath();
        c.arc(last.x, last.y, 6, 0, Math.PI * 2);
        c.fill();
        c.fillStyle = palette.surface;
        c.strokeStyle = color;
        c.lineWidth = 2;
        c.beginPath();
        c.arc(last.x, last.y, 3, 0, Math.PI * 2);
        c.fill();
        c.stroke();
        c.restore();
      },
    };
  }

  ngOnDestroy(): void {
    this.visibility?.disconnect();
    this.chart?.destroy();
  }
}
