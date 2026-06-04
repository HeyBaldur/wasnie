import { Component, computed, input } from '@angular/core';

@Component({
  selector: 'ws-gauge',
  standalone: true,
  templateUrl: './ws-gauge.component.html',
  styleUrl: './ws-gauge.component.scss',
})
export class WsGaugeComponent {
  // 0.0 = 0%, 1.0 = 100%, 1.5 = 150% (overachievement)
  readonly value = input<number>(0);
  readonly label = input('');
  // Fraction of the quota period elapsed (0-1). When provided, shows a pacing target line.
  // null = no pacing line (closed quota or future quota).
  readonly pacingValue = input<number | null>(null);

  private readonly RADIUS = 80;
  private readonly ARC_LENGTH = Math.PI * this.RADIUS; // ≈ 251.33

  // SVG arc path: half-circle from left to right, top half
  readonly trackPath = `M ${100 - this.RADIUS},100 A ${this.RADIUS},${this.RADIUS} 0 0,1 ${100 + this.RADIUS},100`;

  readonly fillDashArray = computed(() => {
    const fill = Math.min(this.value(), 1.0) * this.ARC_LENGTH;
    return `${fill.toFixed(2)} ${this.ARC_LENGTH.toFixed(2)}`;
  });

  readonly fillColor = computed(() => {
    const v = this.value();
    if (v >= 1.0) return 'var(--color-brand)';
    if (v >= 0.80) return 'var(--color-success)';
    if (v >= 0.50) return 'var(--color-warning)';
    return 'var(--color-danger)';
  });

  readonly percentText = computed(() => `${Math.round(this.value() * 100)}%`);

  readonly ariaLabel = computed(() =>
    `Gauge: ${this.percentText()} attainment${this.label() ? ` — ${this.label()}` : ''}`);

  // Pacing marker position on the arc.
  // Arc: starts at (20,100), goes counter-clockwise through top to (180,100).
  // At fraction P: x = 100 - 80*cos(P*π), y = 100 - 80*sin(P*π)
  readonly pacingPoint = computed(() => {
    const p = this.pacingValue();
    if (p === null || p < 0 || p > 1) return null;
    const angle = p * Math.PI;
    const x = 100 - this.RADIUS * Math.cos(angle);
    const y = 100 - this.RADIUS * Math.sin(angle);
    return { x: +x.toFixed(1), y: +y.toFixed(1) };
  });
}
