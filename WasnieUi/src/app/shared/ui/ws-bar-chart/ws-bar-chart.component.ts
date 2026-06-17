import {
  afterNextRender,
  Component,
  computed,
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

Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip);

export interface BarChartPoint {
  label: string;
  value: number;
  currency?: string;
  isCurrent?: boolean;
}

const BLUE_CURRENT = '#3b82f6';              // current month — solid accent
const BLUE_PAST    = 'rgba(59,130,246,0.50)'; // past months — same hue, half opacity
const BLUE_HOVER   = 'rgba(59,130,246,0.80)'; // past hover — slightly more opaque
const TICK_AXIS    = '#9ca3af';               // gray-400 — ticks recede behind titles
const GRID         = 'rgba(0,0,0,0.06)';

/**
 * Vertical bar chart for monthly earnings trends.
 *
 * Replaces the hand-drawn SVG implementation with Chart.js so axis
 * labels, tooltips, and rendering stay consistent with ws-hbar-chart.
 * Current period bar is highlighted in blue; past periods are gray.
 * Y-axis ticks use compact notation (1.2M, 94K, etc.).
 */
@Component({
  selector: 'ws-bar-chart',
  standalone: true,
  templateUrl: './ws-bar-chart.component.html',
  styleUrl: './ws-bar-chart.component.scss',
})
export class WsBarChartComponent implements OnDestroy {
  @ViewChild('canvas', { static: true }) private canvasRef!: ElementRef<HTMLCanvasElement>;

  readonly points    = input<BarChartPoint[]>([]);
  readonly emptyLabel = input('');

  readonly hasData = computed(() => this.points().some(p => p.value > 0));

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  private chart: Chart<'bar', number[], string> | null = null;

  constructor() {
    afterNextRender(() => { if (!this.chart) this.initChart(); });

    effect(() => {
      const pts = this.points();
      const hasValues = pts.some(p => p.value > 0);

      if (!hasValues) return;

      if (!this.chart) {
        // Data arrived after initial render — canvas exists, chart not yet created
        this.initChart();
        return;
      }

      this.chart.data.labels = pts.map(p => p.label);
      this.chart.data.datasets[0].data = pts.map(p => p.value);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (this.chart.data.datasets[0] as any).backgroundColor =
        pts.map(p => p.isCurrent ? BLUE_CURRENT : BLUE_PAST);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (this.chart.data.datasets[0] as any).hoverBackgroundColor =
        pts.map(p => p.isCurrent ? BLUE_CURRENT : BLUE_HOVER);
      this.chart.update('none');
    });
  }

  private initChart(): void {
    const canvas = this.canvasRef.nativeElement;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const pts = this.points();
    if (!pts.some(p => p.value > 0)) return;

    const currency = pts.find(p => p.currency)?.currency ?? '';

    this.chart = new Chart<'bar', number[], string>(canvas, {
      type: 'bar',
      data: {
        labels: pts.map(p => p.label),
        datasets: [{
          data: pts.map(p => p.value),
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          backgroundColor:      pts.map(p => p.isCurrent ? BLUE_CURRENT : BLUE_PAST) as any,
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          hoverBackgroundColor: pts.map(p => p.isCurrent ? BLUE_CURRENT : BLUE_HOVER) as any,
          borderWidth: 0,
          borderRadius: 6,
          minBarLength: 8,
          barPercentage: 0.72,
          categoryPercentage: 0.88,
        }],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        plugins: {
          legend: { display: false },
          tooltip: {
            enabled: true,
            backgroundColor: 'rgba(17, 24, 39, 0.92)',
            titleColor: '#9ca3af',
            bodyColor: '#f9fafb',
            padding: 8,
            cornerRadius: 6,
            displayColors: false,
            callbacks: {
              label: (tooltipCtx) => {
                const val = tooltipCtx.parsed.y ?? 0;
                return ` ${this.fmt(val, currency)}`;
              },
            },
          },
        },
        scales: {
          x: {
            grid:   { display: false },
            border: { display: false },
            ticks: {
              color: TICK_AXIS,
              font:  { size: 11 },
              maxRotation: 0,
            },
          },
          y: {
            grid:   { color: GRID },
            border: { display: false },
            ticks: {
              color: TICK_AXIS,
              font:  { size: 11 },
              maxTicksLimit: 5,
              callback: (val) => this.fmtAxis(Number(val)),
            },
          },
        },
      },
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
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
          notation: 'compact', maximumFractionDigits: 2,
        }).format(value)
      : value.toLocaleString('en-US', { maximumFractionDigits: 0 });
  }

  /** Public for tests and backward compatibility. */
  formatValue(value: number, currency: string): string {
    return this.fmt(value, currency);
  }
}
