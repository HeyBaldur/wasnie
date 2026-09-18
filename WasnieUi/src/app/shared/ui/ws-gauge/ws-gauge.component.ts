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
import { prefersReducedMotion, readChartPalette, type ChartPalette } from '../chart-theme';

Chart.register(DoughnutController, ArcElement, Tooltip);

/**
 * How the arc is coloured.
 *
 * ★★ THE DEFAULT IS A VERDICT AND THE OTHERS ARE NOT, which is the whole reason this input exists.
 * `attainment` climbs danger → warning → success → brand, because falling short of a quota IS bad
 * news and the colour should say so. Anywhere the figure is a SCOPE rather than a score, that ladder
 * lies: a role holding 5 of 31 permissions is not "critical", it is a sales rep, and painting it red
 * tells an administrator to go and fix something that is working exactly as designed.
 */
export type GaugeAccent = 'attainment' | 'brand' | 'blue' | 'violet';

/**
 * A doughnut gauge — Chart.js, like every other chart in this design system.
 *
 * ★★ IT READS TOKENS THROUGH `chart-theme.ts` NOW, AND IT WAS THE ONLY CHART THAT DID NOT. It carried
 * four hardcoded hex values (`#3b82f6`, `#10b981`, `#f59e0b`, `#ef4444`) — literals in a codebase whose
 * §5.5 forbids them, and colours that could not follow a theme because a canvas cannot resolve
 * `var(--…)`. The shared helper solves exactly that: it reads the tokens off the host with
 * getComputedStyle and normalises them through the 2D context.
 *
 * ★★ AND IT CAN DRAW A FULL RING, NOT ONLY A HALF ONE. Fixed at `circumference: 180` it could not be
 * the shape some screens need, which is how a hand-rolled SVG ring ended up in the users panel — an
 * ad-hoc element where a primitive belongs (§5.4). The default is still 180, so nothing that used the
 * half-circle changes.
 *
 * ★ REBUILT RATHER THAN EXTENDED FROM A DISTANCE, AND IT WAS SAFE TO: nothing in the app rendered
 * `<ws-gauge>` at the time. It survived only as a dead import in dashboard.component.ts, left behind
 * when the attainment block moved to `ws-hbar-chart`. Zero call sites, so zero regressions.
 *
 * ★ THE CENTRE LABEL IS SUPPLIED, NOT ASSUMED. It used to print a percentage and nothing else. A
 * percentage is a score; a caller showing "6 / 31" is showing a count, and the two are not
 * interchangeable on a screen about who may do what.
 */
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

  /** 180 = half circle (the original shape, still the default). 360 = a full ring. */
  readonly circumference = input<180 | 360>(180);

  readonly accent = input<GaugeAccent>('attainment');

  /** The big figure in the middle. Omitted → the percentage, as before. */
  readonly label = input<string | null>(null);

  /** A smaller line under it, e.g. the denominator of a count. */
  readonly sublabel = input<string | null>(null);

  /** What a screen reader is told. Omitted → the label, or the percentage. */
  readonly ariaLabel = input<string | null>(null);

  /** API compat: pacing marker not rendered in the Chart.js version. */
  readonly pacingValue = input<number | null>(null);

  private chart: Chart<'doughnut', number[], string> | null = null;

  readonly percentText = computed(() => `${Math.round(this.value() * 100)}%`);

  readonly centreText = computed(() => this.label() ?? this.percentText());

  readonly describedAs = computed(() => this.ariaLabel() ?? this.centreText());

  constructor() {
    afterNextRender(() => { if (!this.chart) this.initChart(); });

    effect(() => {
      // Read every input so the effect re-runs for all of them.
      const pct = Math.min(this.value() * 100, 100);
      const rest = Math.max(0, 100 - pct);
      this.accent();
      const circumference = this.circumference();

      const chart = this.chart;
      if (!chart) return;

      const palette = this.palette();
      if (!palette) return;

      chart.data.datasets[0].data = [pct, rest];
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (chart.data.datasets[0] as any).backgroundColor = [this.arcColor(palette), palette.sunken];
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (chart.options as any).circumference = circumference;
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (chart.options as any).rotation = circumference === 360 ? 0 : -90;
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (chart.options as any).aspectRatio = circumference === 360 ? 1 : 2;
      chart.update();
    });
  }

  /**
   * ★ THE LADDER IS TOKENS NOW, NOT HEX. The thresholds are unchanged — under 50 behind, 80 on track,
   * 100 achieved — so anything that relied on the old reading still gets it, in the theme's colours.
   */
  private arcColor(p: ChartPalette): string {
    switch (this.accent()) {
      case 'brand': return p.blue;
      case 'blue': return p.blue;
      case 'violet': return p.violet;
      default: {
        const pct = this.value() * 100;
        if (pct >= 100) return p.blue;
        if (pct >= 80) return p.success;
        if (pct >= 50) return p.warning;
        return p.danger;
      }
    }
  }

  private palette(): ChartPalette | null {
    const ctx = this.canvasRef?.nativeElement?.getContext('2d');
    if (!ctx) return null;
    return readChartPalette(this.canvasRef.nativeElement, ctx);
  }

  private initChart(): void {
    const canvas = this.canvasRef.nativeElement;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const palette = readChartPalette(canvas, ctx);
    const pct = Math.min(this.value() * 100, 100);
    const rest = Math.max(0, 100 - pct);
    const circumference = this.circumference();

    this.chart = new Chart(canvas, {
      type: 'doughnut',
      data: {
        datasets: [{
          data: [pct, rest],
          backgroundColor: [this.arcColor(palette), palette.sunken],
          borderWidth: 0,
        }],
      },
      options: {
        // A half circle opens at 9 o'clock; a ring is read from 12.
        rotation: circumference === 360 ? 0 : -90,
        circumference,
        cutout: '80%',
        responsive: true,
        maintainAspectRatio: true,
        aspectRatio: circumference === 360 ? 1 : 2,
        plugins: {
          legend: { display: false },
          tooltip: { enabled: false },
        },
        // ★ The sweep is the chart library's, not a CSS keyframe racing it, and it stops when the
        //   reader has asked for less movement.
        animation: prefersReducedMotion()
          ? false
          : { duration: 800, easing: 'easeOutQuart' },
      },
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }
}
