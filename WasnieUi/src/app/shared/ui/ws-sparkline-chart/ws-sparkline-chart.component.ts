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
  Chart,
  CategoryScale,
  Filler,
  LinearScale,
  LineController,
  LineElement,
  PointElement,
  Tooltip,
} from 'chart.js';

Chart.register(CategoryScale, LinearScale, LineController, LineElement, PointElement, Filler, Tooltip);

/**
 * Tiny Chart.js sparkline with hover tooltip.
 *
 * Pass `labels` (e.g. last-7-day date strings) so tooltip shows
 * the day alongside the value. If labels is empty, Chart.js uses
 * point indices. `pointer-events-none` must NOT be on the host
 * element — the parent div should allow mouse events through.
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

  readonly values   = input<number[]>([]);
  readonly labels   = input<string[]>([]);
  readonly color    = input<string>('#10b981');
  readonly currency = input<string>('');

  private chart: Chart<'line', number[], string> | null = null;

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

    const color    = this.color();
    // Capture currency at init time so the tooltip closure is bound to THIS
    // instance's currency and cannot inherit a value from an adjacent sparkline.
    const currency = this.currency();
    const vals     = this.values();
    const lbls     = this.labels();
    const resolvedLabels = lbls.length === vals.length ? lbls : vals.map((_, i) => String(i + 1));

    const grad = ctx.createLinearGradient(0, 0, 0, 60);
    grad.addColorStop(0, `${color}26`);
    grad.addColorStop(1, `${color}00`);

    this.chart = new Chart(canvas, {
      type: 'line',
      data: {
        labels: resolvedLabels,
        datasets: [{
          data: vals,
          borderColor: color,
          borderWidth: 1.5,
          pointRadius: 0,
          pointHoverRadius: 4,
          pointHoverBackgroundColor: color,
          tension: 0.4,
          fill: true,
          backgroundColor: grad,
        }],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        interaction: {
          mode: 'index',
          intersect: false,
        },
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
                // Uses the closure-captured `currency` constant, not this.currency(),
                // to guarantee each sparkline instance formats its own data.
                if (currency) {
                  return ` ${new Intl.NumberFormat('en-US', {
                    style: 'currency', currency,
                    notation: 'compact', maximumFractionDigits: 2,
                  }).format(val)}`;
                }
                return ` ${val.toLocaleString('en-US', { maximumFractionDigits: 0 })}`;
              },
            },
          },
        },
        scales: {
          x: { display: false },
          y: { display: false },
        },
      },
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }
}
