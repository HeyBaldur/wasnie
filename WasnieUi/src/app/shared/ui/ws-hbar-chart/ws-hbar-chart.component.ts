import {
  afterNextRender,
  Component,
  effect,
  ElementRef,
  input,
  OnDestroy,
  ViewChild,
} from '@angular/core';
import {
  BarController,
  BarElement,
  CategoryScale,
  Chart,
  LinearScale,
  Tooltip,
} from 'chart.js';
import type { BarChartPoint } from '../ws-bar-chart/ws-bar-chart.component';

Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip);

const BLUE      = '#3b82f6';
const GRAY      = '#4b5563';
const TICK_LBL  = '#374151';
const TICK_AXIS = '#6b7280';
const GRID      = 'rgba(0,0,0,0.06)';

/**
 * Horizontal bar chart comparing two periods (prior vs current).
 *
 * Single dataset with a per-bar backgroundColor array — Chart.js assigns
 * one colour per row without creating ghost space. Two separate datasets
 * with null values would double each channel height because Chart.js still
 * reserves layout room for the null slot.
 *
 * minBarLength: 8 guarantees the prior bar renders even when its value is
 * orders of magnitude smaller than the current (e.g. €94k vs €5.9T).
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

  readonly points = input<BarChartPoint[]>([]);

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  private chart: Chart<'bar', number[], string> | null = null;

  constructor() {
    afterNextRender(() => this.initChart());

    effect(() => {
      const [prior, current] = this.splitPoints();
      if (!this.chart || !prior || !current) return;
      this.chart.data.labels           = [prior.label, current.label];
      this.chart.data.datasets[0].data = [prior.value, current.value];
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (this.chart.data.datasets[0] as any).backgroundColor = [GRAY, BLUE];
      this.chart.update('none');
    });
  }

  private initChart(): void {
    const canvas = this.canvasRef.nativeElement;
    if (!canvas.getContext('2d')) return;

    const [prior, current] = this.splitPoints();
    if (!prior || !current) return;

    this.chart = new Chart<'bar', number[], string>(canvas, {
      type: 'bar',
      data: {
        labels: [prior.label, current.label],
        datasets: [{
          label: 'Amount',
          data: [prior.value, current.value],
          // Per-bar colours via array: index 0 = prior (gray), index 1 = current (blue)
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          backgroundColor: [GRAY, BLUE] as any,
          borderWidth: 0,
          borderRadius: 4,
          barThickness: 14,
          minBarLength: 8,
        }],
      },
      options: {
        indexAxis: 'y',
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 300 },
        plugins: {
          legend: { display: false },
          tooltip: {
            enabled: true,
            callbacks: {
              label: (ctx) => {
                const pt    = ctx.dataIndex === 1 ? current : prior;
                const value = ctx.parsed.x ?? 0;
                return ` ${this.fmt(value, pt?.currency ?? '')}`;
              },
            },
          },
        },
        scales: {
          x: {
            grid:   { color: GRID },
            border: { display: false },
            ticks: {
              color: TICK_AXIS,
              font:  { size: 11 },
              maxTicksLimit: 5,
              callback: (val) => this.fmtAxis(Number(val)),
            },
          },
          y: {
            grid:   { display: false },
            border: { display: false },
            ticks: {
              color: TICK_LBL,
              font:  { size: 12, weight: 'bold' as const },
            },
          },
        },
      },
    });
  }

  ngOnDestroy(): void {
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
}
