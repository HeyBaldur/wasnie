import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';
import { WsTableComponent } from './ws-table.component';
import { MAX_FIT_WIDTH, MIN_COLUMN_WIDTH, WsResizableColumnsDirective } from './ws-resizable-columns.directive';

const KEY = 'ws-table:cols:spec';
const LONG_REF = 'TX-2026-09-19-HUBSPOT-DEAL-0000000000000000000000000000000000000000000000000000000000000001';

@Component({
  standalone: true,
  imports: [WsTableComponent, WsResizableColumnsDirective],
  // A fixed-width host, so "the filler takes what is left" has a known answer.
  styles: [':host { display: block; width: 900px; }'],
  template: `
    <ws-table>
      <table wsResizableColumns="spec">
        <thead>
          <tr>
            <th data-col="ref" data-width="200">Reference</th>
            <th data-col="amount" data-width="120">Amount</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <div class="ws-cell-line">
                <a class="ws-cell-text" href="#">{{ ref }}</a>
                <button class="copy" type="button">c</button>
              </div>
            </td>
            <td>1,00</td>
            <td><button type="button">Void</button></td>
          </tr>
        </tbody>
      </table>
    </ws-table>
  `,
})
class HostComponent {
  ref = LONG_REF;
}

/**
 * ★★ REAL LAYOUT, NOT JSDOM (§A2). Karma runs in Chrome, so every width below is a measured pixel,
 * not a style string that nobody rendered — the directive exists precisely to change geometry, and a
 * test that only read `style.width` back would pass even if the browser ignored it.
 */
describe('WsResizableColumnsDirective', () => {
  let fixture: ComponentFixture<HostComponent>;

  const th = (i: number) => fixture.nativeElement.querySelectorAll('th')[i] as HTMLTableCellElement;
  const width = (el: Element) => Math.round(el.getBoundingClientRect().width);
  const handle = (i: number) => th(i).querySelector('.ws-col-resizer') as HTMLElement;

  async function mount(): Promise<void> {
    fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    await fixture.whenStable();
    // afterNextRender runs after the first render; give it one frame.
    await new Promise(r => requestAnimationFrame(() => r(null)));
  }

  function drag(i: number, dx: number): void {
    const h = handle(i);
    const x = h.getBoundingClientRect().right;
    h.dispatchEvent(new PointerEvent('pointerdown', { button: 0, clientX: x, pointerId: 1, bubbles: true }));
    h.dispatchEvent(new PointerEvent('pointermove', { clientX: x + dx, pointerId: 1, bubbles: true }));
    h.dispatchEvent(new PointerEvent('pointerup', { clientX: x + dx, pointerId: 1, bubbles: true }));
  }

  beforeEach(async () => {
    localStorage.removeItem(KEY);
    await TestBed.configureTestingModule({
      imports: [HostComponent, TranslateModule.forRoot()],
    }).compileComponents();
  });

  afterEach(() => {
    fixture?.nativeElement.remove();
    localStorage.removeItem(KEY);
  });

  it('starts every resizable column at its declared width', async () => {
    await mount();

    expect(width(th(0))).toBe(200);
    expect(width(th(1))).toBe(120);
  });

  it('gives the last column whatever the others leave, so the table spans its container', async () => {
    await mount();

    const wrap = fixture.nativeElement.querySelector('.ws-table-wrap') as HTMLElement;
    expect(width(th(2))).toBe(wrap.clientWidth - 320);
  });

  it('offers a handle on every resizable column, but not on the filler', async () => {
    await mount();

    expect(handle(0)).withContext('reference').toBeTruthy();
    expect(handle(1)).withContext('amount').toBeTruthy();
    expect(handle(2)).withContext('filler').toBeNull();
  });

  it('dragging the edge resizes the column and remembers the width', async () => {
    await mount();

    drag(0, 90);

    expect(width(th(0))).toBe(290);
    expect(JSON.parse(localStorage.getItem(KEY)!)).toEqual({ ref: 290, amount: 120 });
  });

  it('never goes narrower than the minimum', async () => {
    await mount();

    drag(1, -500);

    expect(width(th(1))).toBe(MIN_COLUMN_WIDTH);
  });

  it('restores the remembered widths on the next visit', async () => {
    localStorage.setItem(KEY, JSON.stringify({ ref: 333 }));
    await mount();

    expect(width(th(0))).toBe(333);
    expect(width(th(1))).withContext('not remembered → declared').toBe(120);
  });

  it('arrow keys on a focused handle resize it too', async () => {
    await mount();

    handle(1).dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));

    expect(width(th(1))).toBe(136);
  });

  it('double-click fits the content, capped so one huge value cannot take the screen', async () => {
    await mount();

    handle(0).dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));

    expect(width(th(0))).toBeGreaterThan(200);
    expect(width(th(0))).toBeLessThanOrEqual(MAX_FIT_WIDTH);
  });

  /**
   * ★★ THE DEFECT THAT STARTED THIS. A long reference used to push its copy button onto a line of its
   * own. Now the text truncates and the button stays on the same line, inside the cell.
   */
  it('keeps the trailing button on the text line and inside the cell when the text is too long', async () => {
    await mount();

    const cell = fixture.nativeElement.querySelector('td') as HTMLElement;
    const text = cell.querySelector('.ws-cell-text') as HTMLElement;
    const button = cell.querySelector('.copy') as HTMLElement;
    const c = cell.getBoundingClientRect();
    const t = text.getBoundingClientRect();
    const b = button.getBoundingClientRect();

    expect(text.scrollWidth).withContext('the text really is truncated').toBeGreaterThan(text.clientWidth);
    expect(Math.abs(b.top + b.height / 2 - (t.top + t.height / 2))).withContext('same line').toBeLessThan(2);
    expect(b.right).withContext('inside the cell').toBeLessThanOrEqual(c.right);
  });
});
