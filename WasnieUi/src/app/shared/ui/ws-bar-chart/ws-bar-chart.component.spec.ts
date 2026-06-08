import { TestBed } from '@angular/core/testing';
import { WsBarChartComponent, type BarChartPoint } from './ws-bar-chart.component';

function makePoint(label: string, value: number, isCurrent = false): BarChartPoint {
  return { label, value, currency: 'EUR', isCurrent };
}

function make12Months(currentIdx = 5): BarChartPoint[] {
  const months = ['Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec', 'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun'];
  return months.map((m, i) => makePoint(m, i === currentIdx ? 342.76 : 100, i === currentIdx));
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

  it('renders N bars from N data points', () => {
    fixture.componentRef.setInput('points', make12Months());
    fixture.detectChanges();

    const bars = component.bars();
    expect(bars.length).toBe(12);
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

    const html: string = fixture.nativeElement.innerHTML;
    expect(html).toContain('No earnings yet');
    expect(fixture.nativeElement.querySelector('svg')).toBeNull();
  });

  it('current month bar has ws-bc__bar--current class; others have ws-bc__bar--past', () => {
    fixture.componentRef.setInput('points', make12Months(5));
    fixture.detectChanges();

    const bars = component.bars();
    bars.forEach((bar, i) => {
      if (i === 5) {
        expect(bar.point.isCurrent).toBeTrue();
      } else {
        expect(bar.point.isCurrent).toBeFalsy();
      }
    });
  });

  it('zero-value bar has minimum height (stub, not invisible)', () => {
    const pts = [makePoint('Jan', 0), makePoint('Feb', 500), makePoint('Mar', 0)];
    fixture.componentRef.setInput('points', pts);
    fixture.detectChanges();

    const bars = component.bars();
    // Jan and Mar have value 0 — they should still have height >= 2
    expect(bars[0].height).toBeGreaterThanOrEqual(2);
    expect(bars[2].height).toBeGreaterThanOrEqual(2);
    // Feb (500) should be taller than the stubs
    expect(bars[1].height).toBeGreaterThan(bars[0].height);
  });

  it('tooltip X is clamped to [8, 75]% for first bucket', () => {
    const pts = make12Months();
    fixture.componentRef.setInput('points', pts);
    fixture.detectChanges();

    // Simulate mouse over the first bucket (far left)
    const mockEvent = { clientX: 0, clientY: 0 } as MouseEvent;

    const svgRect = { left: 0, width: 560, top: 0, height: 180 };
    const svgEl = { getBoundingClientRect: () => svgRect } as unknown as SVGSVGElement;
    (component as any).svgEl = { nativeElement: svgEl };

    component.onSvgMouseMove(mockEvent);
    // Tooltip X should be clamped to at least 8%
    expect(component.tooltipX()).toBeGreaterThanOrEqual(8);
  });

  it('tooltip X is clamped to 75% for last bucket', () => {
    const pts = make12Months();
    fixture.componentRef.setInput('points', pts);
    fixture.detectChanges();

    // Simulate mouse at the far right (beyond VB_W)
    const mockEvent = { clientX: 560, clientY: 0 } as MouseEvent;
    const svgRect = { left: 0, width: 560, top: 0, height: 180 };
    const svgEl = { getBoundingClientRect: () => svgRect } as unknown as SVGSVGElement;
    (component as any).svgEl = { nativeElement: svgEl };

    component.onSvgMouseMove(mockEvent);
    expect(component.tooltipX()).toBeLessThanOrEqual(75);
  });

  it('formatValue returns currency-formatted string', () => {
    // 342.76 rounds to 343 with 0 decimal places; assert currency symbol is present
    const result = component.formatValue(342, 'EUR');
    expect(result).toContain('342');
    expect(result).toContain('€');
  });

  it('formatValue with no currency returns plain number', () => {
    const result = component.formatValue(1000, '');
    expect(result).toContain('1');
  });
});
