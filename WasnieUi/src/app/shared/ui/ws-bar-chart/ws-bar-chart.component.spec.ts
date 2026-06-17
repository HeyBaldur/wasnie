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

  // ── Data detection ─────────────────────────────────────────────────────────

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

  it('hasData is false when points is empty', () => {
    fixture.componentRef.setInput('points', []);
    fixture.detectChanges();

    expect(component.hasData()).toBeFalse();
  });

  // ── Empty state ────────────────────────────────────────────────────────────

  it('renders empty label when no data', () => {
    fixture.componentRef.setInput('points', []);
    fixture.componentRef.setInput('emptyLabel', 'No earnings yet');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('svg')).toBeNull();
    expect(fixture.nativeElement.innerHTML).toContain('No earnings yet');
  });

  it('does not render empty label when data is present', () => {
    fixture.componentRef.setInput('points', make12Months());
    fixture.componentRef.setInput('emptyLabel', 'No earnings yet');
    fixture.detectChanges();

    expect(fixture.nativeElement.innerHTML).not.toContain('No earnings yet');
  });

  // ── Format helpers ─────────────────────────────────────────────────────────

  it('formatValue returns currency-formatted string', () => {
    const result = component.formatValue(342, 'EUR');
    expect(result).toContain('€');
  });

  it('formatValue with no currency returns plain number', () => {
    const result = component.formatValue(1000, '');
    expect(result).toContain('1');
  });

  it('formatValue uses compact notation for large amounts', () => {
    const result = component.formatValue(5_929_711_576_736, 'EUR');
    // compact: should contain T (trillion) — never show the full multi-digit string
    expect(result).toContain('T');
    expect(result.length).toBeLessThan(10);
  });
});
