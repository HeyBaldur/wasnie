/**
 * The chart palette, shared by ws-bar-chart, ws-hbar-chart and ws-sparkline-chart.
 *
 * ★ TOKENS, READ AT BUILD TIME. A canvas cannot resolve `var(--…)`, so each chart reads the design tokens off
 * its own host with getComputedStyle and normalises them through the 2D context — the same colours as the rest
 * of the app, in every theme, with no literals in the charts. (A theme switched while the page is open keeps
 * the old colours until the chart is rebuilt.)
 */
export interface ChartPalette {
  violet: string;
  blue: string;
  success: string;
  /** Added for ws-gauge's attainment ladder, which carried these two as hardcoded hex. */
  warning: string;
  danger: string;
  text: string;
  tick: string;
  label: string;
  grid: string;
  surface: string;
  sunken: string;
  inverse: string;
}

export function readChartPalette(host: HTMLElement, ctx: CanvasRenderingContext2D): ChartPalette {
  const styles = getComputedStyle(host);
  const token = (name: string, fallback: string) => normaliseColor(ctx, styles.getPropertyValue(name).trim() || fallback);
  return {
    violet: token('--color-accent-violet', '#8b5cf6'),
    blue: token('--color-accent-blue', '#3b82f6'),
    success: token('--color-success', '#10b981'),
    warning: token('--color-warning', '#f59e0b'),
    danger: token('--color-danger', '#ef4444'),
    text: token('--color-text-primary', '#111827'),
    tick: token('--color-text-tertiary', '#9ca3af'),
    label: token('--color-text-secondary', '#4b5563'),
    grid: withAlpha(token('--color-border-default', '#e5e7eb'), 0.7),
    surface: token('--color-bg-surface', '#ffffff'),
    sunken: token('--color-bg-surface-sunken', '#f3f4f6'),
    inverse: token('--color-text-inverse', '#ffffff'),
  };
}

/** Any CSS colour → the canvas's own "#rrggbb" / "rgba(…)" form, so alpha can be applied to it. */
export function normaliseColor(ctx: CanvasRenderingContext2D, color: string): string {
  ctx.fillStyle = '#000';
  ctx.fillStyle = color;
  return ctx.fillStyle as string;
}

export function withAlpha(color: string, a: number): string {
  const hex = /^#([0-9a-f]{6})$/i.exec(color);
  if (hex) {
    const n = parseInt(hex[1], 16);
    return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${a})`;
  }
  const rgb = /^rgba?\(([^,]+),([^,]+),([^,)]+)/i.exec(color);
  return rgb ? `rgba(${rgb[1].trim()}, ${rgb[2].trim()}, ${rgb[3].trim()}, ${a})` : color;
}

export function prefersReducedMotion(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** The tooltip every chart shares: an inverted chip with the label muted and the amount bold. */
export function chartTooltip(palette: ChartPalette) {
  return {
    enabled: true,
    backgroundColor: palette.text,
    titleColor: withAlpha(palette.inverse, 0.7),
    bodyColor: palette.inverse,
    titleFont: { size: 11, weight: 500 as const },
    bodyFont: { size: 13, weight: 700 as const },
    padding: { x: 12, y: 8 },
    cornerRadius: 8,
    caretSize: 5,
    displayColors: false,
  };
}

/**
 * Replays a chart's entrance the first time its canvas is actually visible — charts often render inside a
 * collapsed card or below the fold, where the first animation plays unseen. Returns the observer to disconnect.
 */
export function replayWhenVisible(canvas: HTMLCanvasElement, replay: () => void): IntersectionObserver | null {
  if (prefersReducedMotion() || typeof IntersectionObserver === 'undefined') return null;
  let played = false;
  const observer = new IntersectionObserver((entries) => {
    if (played || !entries.some(e => e.isIntersecting && e.intersectionRect.height > 0)) return;
    played = true;
    replay();
    observer.disconnect();
  }, { threshold: 0.2 });
  observer.observe(canvas);
  return observer;
}
