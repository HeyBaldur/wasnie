import {
  afterNextRender,
  Component,
  effect,
  ElementRef,
  inject,
  input,
  OnDestroy,
  output,
  ViewChild,
} from '@angular/core';
import {
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
import type { BarChartPoint } from '../ws-bar-chart/ws-bar-chart.component';
import {
  ChartPalette,
  chartTooltip,
  prefersReducedMotion,
  readChartPalette,
  replayWhenVisible,
  withAlpha,
} from '../chart-theme';

Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip);

/**
 * Horizontal bar chart comparing two periods (prior vs current).
 *
 * Single dataset with per-bar styling — Chart.js assigns one style per row without creating ghost space. Two
 * separate datasets with null values would double each channel height because Chart.js still reserves layout
 * room for the null slot.
 *
 * minBarLength guarantees the prior bar renders even when its value is orders of magnitude smaller than the
 * current (e.g. €94k vs €5.9T).
 *
 * ★ SAME LANGUAGE AS THE PAYEE'S SALES TREND (ws-bar-chart): the current period is a blue→violet gradient that
 * glows, the prior one a soft neutral bar; each bar carries its amount at its end; hovering lights the row; the
 * bars grow in from the axis the first time the chart is seen. Colours come from the design tokens.
 */
@Component({
  selector: 'ws-hbar-chart',
  standalone: true,
  template: `<canvas #canvas></canvas>`,
  styles: [`:host { display: block; width: 100%; height: 100%; }
            canvas { display: block; width: 100% !important; height: 100% !important; }`],
})
export class WsHBarChartComponent implements OnDestroy {
  @ViewChild('canvas', { static: true }) private canvasRef!: ElementRef<HTMLCanvasElement>;
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly points = input<BarChartPoint[]>([]);

  /**
   * Emitted when a bar is clicked, with the point that bar represents.
   * Consumers use it to drill from a figure down to the rows that make it up; the chart itself stays
   * navigation-agnostic (no Router in a shared primitive).
   */
  readonly barClick = output<BarChartPoint>();

  private chart: Chart<'bar', number[], string> | null = null;
  private visibility: IntersectionObserver | null = null;

  constructor() {
    afterNextRender(() => { if (!this.chart) this.initChart(); });

    effect(() => {
      const [prior, current] = this.splitPoints();
      if (!this.chart || !prior || !current) return;
      this.chart.data.labels           = [prior.label, current.label];
      this.chart.data.datasets[0].data = [prior.value, current.value];
      this.chart.update('none');
    });
  }

  private initChart(): void {
    const canvas = this.canvasRef.nativeElement;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const [prior, current] = this.splitPoints();
    if (!prior || !current) return;

    const palette = readChartPalette(this.host.nativeElement, ctx);
    const reducedMotion = prefersReducedMotion();

    const barFill = (context: ScriptableContext<'bar'>, hover: boolean): CanvasGradient | string => {
      const area = context.chart.chartArea as ChartArea | undefined;
      if (!area) return palette.blue;
      const g = context.chart.ctx.createLinearGradient(area.left, 0, area.right, 0);
      if (context.dataIndex === 1) {
        g.addColorStop(0, palette.blue);
        g.addColorStop(1, palette.violet);
      } else {
        g.addColorStop(0, withAlpha(palette.tick, hover ? 0.55 : 0.35));
        g.addColorStop(1, withAlpha(palette.tick, hover ? 0.8 : 0.6));
      }
      return g;
    };

    this.chart = new Chart<'bar', number[], string>(canvas, {
      type: 'bar',
      data: {
        labels: [prior.label, current.label],
        datasets: [{
          label: 'Amount',
          data: [prior.value, current.value],
          backgroundColor: (c) => barFill(c, false),
          hoverBackgroundColor: (c) => barFill(c, true),
          borderWidth: 0,
          borderRadius: 999,
          borderSkipped: false,
          barThickness: 16,
          minBarLength: 10,
        }],
      },
      options: {
        indexAxis: 'y',
        responsive: true,
        maintainAspectRatio: false,
        // Room at the end of the longest bar for its amount label.
        layout: { padding: { right: 64 } },
        interaction: { mode: 'nearest', axis: 'y', intersect: false },
        animation: reducedMotion
          ? false
          : {
              duration: 900,
              easing: 'easeOutQuart',
              delay: (c) => (c.type === 'data' && c.mode === 'default' ? c.dataIndex * 140 : 0),
            },
        // Bars are drill-down handles. The points are re-read from the input rather than captured from
        // the closure, so a click always resolves against the data currently rendered.
        onClick: (_evt, elements) => {
          const index = elements[0]?.index;
          if (index === undefined) return;
          const [p, c] = this.splitPoints();
          const point = index === 1 ? c : p;
          if (point) this.barClick.emit(point);
        },
        // Affordance: the cursor only turns into a pointer while actually over a bar.
        onHover: (evt, elements) => {
          const target = evt.native?.target as HTMLElement | undefined;
          if (target) target.style.cursor = elements.length > 0 ? 'pointer' : 'default';
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            ...chartTooltip(palette),
            callbacks: {
              label: (tooltipCtx) => {
                const [p, c] = this.splitPoints();
                const pt = tooltipCtx.dataIndex === 1 ? c : p;
                return this.fmt(tooltipCtx.parsed.x ?? 0, pt?.currency ?? '');
              },
            },
          },
        },
        scales: {
          x: {
            grid:   { color: palette.grid, tickBorderDash: [4, 4] },
            border: { display: false, dash: [4, 4] },
            ticks: {
              color: palette.tick,
              font:  { size: 11 },
              maxTicksLimit: 5,
              callback: (val) => this.fmtAxis(Number(val)),
            },
          },
          y: {
            grid:   { display: false },
            border: { display: false },
            ticks: {
              color: (c) => (c.index === 1 ? palette.violet : palette.label),
              font:  (c) => ({ size: 12, weight: c.index === 1 ? 700 : 600 }),
            },
          },
        },
      },
      plugins: [this.hoverRow(palette), this.currentGlow(palette), this.valueLabels(palette)],
    });

    this.visibility?.disconnect();
    this.visibility = replayWhenVisible(canvas, () => {
      this.chart?.reset();
      this.chart?.update();
    });
  }

  // ── Plugins ────────────────────────────────────────────────────────────────

  /** A soft band behind the hovered row. */
  private hoverRow(palette: ChartPalette): Plugin<'bar'> {
    return {
      id: 'wsHoverRow',
      beforeDatasetsDraw: (chart) => {
        const active = chart.getActiveElements();
        if (!active.length) return;
        const bar = chart.getDatasetMeta(0).data[active[0].index] as unknown as { y: number; height: number };
        const { left, right } = chart.chartArea;
        const height = bar.height * 2;
        const c = chart.ctx;
        c.save();
        c.fillStyle = withAlpha(palette.violet, 0.08);
        c.beginPath();
        c.roundRect(left, bar.y - height / 2, right - left, height, 8);
        c.fill();
        c.restore();
      },
    };
  }

  /** The current period's bar glows. */
  private currentGlow(palette: ChartPalette): Plugin<'bar'> {
    return {
      id: 'wsCurrentGlow',
      afterDatasetDraw: (chart) => {
        const bar = chart.getDatasetMeta(0).data[1] as unknown as { draw: (ctx: CanvasRenderingContext2D) => void } | undefined;
        if (!bar) return;
        const c = chart.ctx;
        c.save();
        c.shadowColor = withAlpha(palette.violet, 0.45);
        c.shadowBlur = 14;
        c.shadowOffsetY = 3;
        bar.draw(c);
        c.restore();
      },
    };
  }

  /** Each bar's amount, just past its end. */
  private valueLabels(palette: ChartPalette): Plugin<'bar'> {
    return {
      id: 'wsValueLabels',
      afterDatasetsDraw: (chart) => {
        const [prior, current] = this.splitPoints();
        const c = chart.ctx;
        c.save();
        c.textBaseline = 'middle';
        [prior, current].forEach((pt, i) => {
          const bar = chart.getDatasetMeta(0).data[i] as unknown as { x: number; y: number } | undefined;
          if (!pt || !bar) return;
          c.font = `${i === 1 ? 700 : 600} 11px system-ui, -apple-system, "Segoe UI", sans-serif`;
          c.fillStyle = i === 1 ? palette.violet : palette.label;
          c.fillText(this.fmtCompact(pt.value, pt.currency ?? ''), bar.x + 8, bar.y);
        });
        c.restore();
      },
    };
  }

  ngOnDestroy(): void {
    this.visibility?.disconnect();
    this.chart?.destroy();
  }

  private splitPoints(): [BarChartPoint | undefined, BarChartPoint | undefined] {
    const pts = this.points();
    return [pts.find(p => !p.isCurrent), pts.find(p => p.isCurrent)];
  }

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
          minimumFractionDigits: 0, maximumFractionDigits: 0,
        }).format(value)
      : value.toLocaleString('en-US', { maximumFractionDigits: 0 });
  }

  private fmtCompact(value: number, currency: string): string {
    return currency
      ? new Intl.NumberFormat('en-US', {
          style: 'currency', currency, notation: 'compact', maximumFractionDigits: 1,
        }).format(value)
      : new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 }).format(value);
  }
}
