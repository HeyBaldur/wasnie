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
import { ArcElement, Chart, DoughnutController, Tooltip } from 'chart.js';

Chart.register(DoughnutController, ArcElement, Tooltip);

@Component({
  selector: 'ws-gauge',
  standalone: true,
  templateUrl: './ws-gauge.component.html',
  styleUrl: './ws-gauge.component.scss',
})
export class WsGaugeComponent implements OnDestroy {
  @ViewChild('canvas', { static: true }) private canvasRef!: ElementRef<HTMLCanvasElement>;

  /** Fraction: 0.0 = 0 %, 1.0 = 100 %, 1.5 = 150 %. */
  readonly value = input<number>(0);
  /** API compat: pacing marker not rendered in the Chart.js version. */
  readonly pacingValue = input<number | null>(null);

  private chart: Chart<'doughnut', number[], string> | null = null;

  /** Color follows business rules: <50 red, 50–79 amber, 80–99 green, ≥100 brand blue. */
  readonly fillColor = computed(() => {
    const pct = this.value() * 100;
    if (pct >= 100) return '#3b82f6'; // brand blue — achieved / exceeded
    if (pct >= 80)  return '#10b981'; // emerald — on track
    if (pct >= 50)  return '#f59e0b'; // amber — in progress
    return '#ef4444';                  // red — critical / behind
  });

  readonly percentText = computed(() => `${Math.round(this.value() * 100)}%`);

  constructor() {
    afterNextRender(() => { if (!this.chart) this.initChart(); });

    effect(() => {
      const pct   = Math.min(this.value() * 100, 100);
      const rest  = Math.max(0, 100 - pct);
      const color = this.fillColor();
      if (!this.chart) return;
      this.chart.data.datasets[0].data = [pct, rest];
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (this.chart.data.datasets[0] as any).backgroundColor = [color, this.trackColor()];
      this.chart.update();
    });
  }

  private initChart(): void {
    const canvas = this.canvasRef.nativeElement;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const pct  = Math.min(this.value() * 100, 100);
    const rest = Math.max(0, 100 - pct);

    this.chart = new Chart(canvas, {
      type: 'doughnut',
      data: {
        datasets: [{
          data: [pct, rest],
          backgroundColor: [this.fillColor(), this.trackColor()],
          borderWidth: 0,
        }],
      },
      options: {
        rotation: -90,       // arc starts at 9 o'clock (left)
        circumference: 180,  // half-circle: left → top → right
        cutout: '80%',
        responsive: true,
        maintainAspectRatio: true,
        aspectRatio: 2,      // 2:1 canvas → arc fills the full width cleanly
        plugins: {
          legend:  { display: false },
          tooltip: { enabled: false },
        },
        animation: {
          duration: 800,
          easing:   'easeOutQuart',
        },
      },
    });
  }

  /** Reads the design-system token so the track respects dark / light mode. */
  private trackColor(): string {
    const raw = getComputedStyle(this.canvasRef.nativeElement)
      .getPropertyValue('--color-border-default').trim();
    return raw || '#e5e7eb';
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }
}
