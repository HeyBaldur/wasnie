import { TestBed } from '@angular/core/testing';
import { WsBarChartComponent, type BarChartPoint } from './ws-bar-chart.component';

function makePoint(label: string, value: number, isCurrent = false): BarChartPoint {
  return { label, value, currency: 'EUR', isCurrent };
}

function make12Months(currentIdx = 5): BarChartPoint[] {
  const months = ['Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec', 'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun'];
  return months.map((m, i) => makePoint(m, i === currentIdx ? 342.76 : 100, i === currentIdx));
}

/** Simulate a mousemove over a given SVG X position. */
function moveMouse(component: WsBarChartComponent, svgX: number): void {
  const mockRect = { left: 0, width: 560, top: 0, height: 180 };
  const svgEl = { getBoundingClientRect: () => mockRect } as unknown as SVGSVGElement;
  (component as any).svgEl = { nativeElement: svgEl };
  component.onSvgMouseMove({ clientX: svgX, clientY: 0 } as MouseEvent);
}

describe('WsBarChartComponent', () => {
  let fixture: ReturnType<typeof TestBed.createComponent<WsBarChartComponent>>;
  let component: WsBarChartComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WsBarChartComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(WsBarChartComponent);
    component = fixture.componentInstance;
  });

  // ── Core rendering ─────────────────────────────────────────────────────────

  it('renders N bars from N data points', () => {
    fixture.componentRef.setInput('points', make12Months());
    fixture.detectChanges();

    expect(component.bars().length).toBe(12);
  });

  it('hasData is false when all values are 0', () => {
    const allZero = make12Months().map(p => ({ ...p, value: 0 }));
    fixture.componentRef.setInput('points', allZero);
    fixture.detectChanges();

    expect(component.hasData()).toBeFalse();
  });

  it('hasData is true when at least one bar has value > 0', () => {
    fixture.componentRef.setInput('points', make12Months());
    fixture.detectChanges();

    expect(component.hasData()).toBeTrue();
  });

  it('renders empty state when points is empty', () => {
    fixture.componentRef.setInput('points', []);
    fixture.componentRef.setInput('emptyLabel', 'No earnings yet');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('svg')).toBeNull();
    expect(fixture.nativeElement.innerHTML).toContain('No earnings yet');
  });

  // ── Current month highlight ────────────────────────────────────────────────

  it('current month bar has isCurrent = true; all others false', () => {
    fixture.componentRef.setInput('points', make12Months(5));
    fixture.detectChanges();

    component.bars().forEach((bar, i) => {
      if (i === 5) {
        expect(bar.point.isCurrent).toBeTrue();
      } else {
        expect(bar.point.isCurrent).toBeFalsy();
      }
    });
  });

  // ── Zero-value bars ────────────────────────────────────────────────────────

  it('zero-value bar has minimum height stub (not invisible)', () => {
    const pts = [makePoint('Jan', 0), makePoint('Feb', 500), makePoint('Mar', 0)];
    fixture.componentRef.setInput('points', pts);
    fixture.detectChanges();

    const bars = component.bars();
    expect(bars[0].height).toBeGreaterThanOrEqual(2); // Jan stub
    expect(bars[2].height).toBeGreaterThanOrEqual(2); // Mar stub
    expect(bars[1].height).toBeGreaterThan(bars[0].height); // Feb is taller
  });

  // ── Tooltip positioning ────────────────────────────────────────────────────

  it('hovering first bucket (left edge): uses left anchor', () => {
    fixture.componentRef.setInput('points', make12Months());
    fixture.detectChanges();

    // First bucket center ≈ 52 + 0*41 + 20 = 72 SVG units → 12.9% of 560
    moveMouse(component, 72);

    expect(component.tooltipVisible()).toBeTrue();
    expect(component.tooltipLeft()).not.toBeNull(); // left-anchored
    expect(component.tooltipRight()).toBeNull();
  });

  it('hovering last bucket (right edge): uses right anchor to prevent overflow', () => {
    fixture.componentRef.setInput('points', make12Months());
    fixture.detectChanges();

    // Last bucket (index 11) center ≈ 52 + 11*41 + 20 = 523 SVG units → 93.4% of 560 → right-anchor
    moveMouse(component, 523);

    expect(component.tooltipVisible()).toBeTrue();
    expect(component.tooltipRight()).not.toBeNull(); // right-anchored
    expect(component.tooltipLeft()).toBeNull();
  });

  it('tooltip top is computed from bar position, not fixed', () => {
    const pts = [makePoint('Jan', 0), makePoint('Feb', 500)];
    fixture.componentRef.setInput('points', pts);
    fixture.detectChanges();

    // Hover Jan (value 0 → short stub, bar top near chart bottom)
    moveMouse(component, 52 + 0 * (component.chartW() / 2) + component.bucketW() / 2);
    const topJan = component.tooltipTop();

    // Hover Feb (value 500 → tall bar, bar top near chart top)
    moveMouse(component, 52 + 1 * (component.chartW() / 2) + component.bucketW() / 2);
    const topFeb = component.tooltipTop();

    // Taller bar means bar.y is smaller → tooltipTop should be smaller (closer to top of card)
    expect(topFeb).toBeLessThan(topJan);
  });

  it('column indicator X is set when hovering', () => {
    fixture.componentRef.setInput('points', make12Months());
    fixture.detectChanges();

    moveMouse(component, 200);
    expect(component.columnIndicatorX()).not.toBeNull();

    component.onSvgMouseLeave();
    expect(component.columnIndicatorX()).toBeNull();
  });

  // ── Format helpers ─────────────────────────────────────────────────────────

  it('formatValue returns currency-formatted string', () => {
    const result = component.formatValue(342, 'EUR');
    expect(result).toContain('342');
    expect(result).toContain('€');
  });

  it('formatValue with no currency returns plain number', () => {
    const result = component.formatValue(1000, '');
    expect(result).toContain('1');
  });
});
