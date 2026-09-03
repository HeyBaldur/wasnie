import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { WsDatePickerComponent } from './ws-date-picker.component';

/**
 * The calendar is position:fixed, so it is measured against the viewport. When an ancestor scrolls,
 * the trigger moves and the calendar has to be re-measured or it is left behind, floating over
 * unrelated rows.
 *
 * ★★ THE BUG THIS FILE EXISTS FOR WAS NOT A MISSING SCROLL LISTENER. The listener was there and it
 * fired; `computePlacement()` re-measured and assigned new coordinates. They just landed in PLAIN
 * FIELDS, and `popoverStyle` is a computed() — which only recomputes when a signal it read changes.
 * So the style kept the numbers from the moment the calendar opened. Everything looked wired and
 * nothing moved.
 *
 * That is why these tests assert the rendered STYLE and never the internal fields: a test that read
 * the fields would have passed against the broken build (§A2).
 */
describe('WsDatePickerComponent — the calendar follows its trigger', () => {
  let fixture: ComponentFixture<WsDatePickerComponent>;
  let component: WsDatePickerComponent;

  /** Pins the host's measured rect, so a "scroll" is just a different answer from the same call. */
  function placeTriggerAt(top: number): void {
    const host = fixture.nativeElement as HTMLElement;
    spyOn(host, 'getBoundingClientRect').and.returnValue({
      top, bottom: top + 40, left: 120, right: 400, width: 280, height: 40, x: 120, y: top,
      toJSON: () => ({}),
    } as DOMRect);
  }

  /**
   * One scroll, plus the rAF the component throttles its reposition with.
   *
   * The event is dispatched on `document` and allowed to bubble, which is what a real nested scroll
   * container produces: the component listens on `window` in the CAPTURE phase precisely so it sees
   * scrolls from containers it does not know about.
   */
  async function scrollViewport(): Promise<void> {
    document.dispatchEvent(new Event('scroll', { bubbles: true }));
    await new Promise((resolve) => requestAnimationFrame(() => resolve(null)));
    await new Promise((resolve) => requestAnimationFrame(() => resolve(null)));
    fixture.detectChanges();
  }

  // A fixed viewport height, because the placement decides up-versus-down from the space available
  // and the karma iframe is short enough to flip that choice. Pinning it keeps these tests about
  // "does it follow the trigger" instead of "how tall is the runner's window".
  let originalInnerHeight: number;

  beforeEach(async () => {
    originalInnerHeight = window.innerHeight;
    Object.defineProperty(window, 'innerHeight', { value: 1000, configurable: true });

    await TestBed.configureTestingModule({
      // ws-button (Today / Clear) carries a RouterLink, so the calendar's own template needs a
      // router present even though nothing here navigates.
      imports: [WsDatePickerComponent, TranslateModule.forRoot()],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(WsDatePickerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    component.close();
    Object.defineProperty(window, 'innerHeight', { value: originalInnerHeight, configurable: true });
  });

  it('pins itself under the trigger when it opens', () => {
    placeTriggerAt(400);
    component.open();
    fixture.detectChanges();

    const style = component.popoverStyle();
    expect(style['position']).toBe('fixed');
    expect(style['top']).toBe('444px');   // trigger bottom + 4
    expect(style['left']).toBe('120px');
  });

  /**
   * ★★ THE REGRESSION. Open low on the page, then scroll so the trigger sits near the top: the
   * calendar has to move with it. Against the broken build this stayed at 444px — exactly the
   * screenshot that reported the bug, with the calendar stranded in the middle of the table.
   */
  it('re-measures and moves when an ancestor scrolls', async () => {
    placeTriggerAt(400);
    component.open();
    fixture.detectChanges();
    expect(component.popoverStyle()['top']).toBe('444px');

    (fixture.nativeElement.getBoundingClientRect as jasmine.Spy).and.returnValue({
      top: 40, bottom: 80, left: 120, right: 400, width: 280, height: 40, x: 120, y: 40,
      toJSON: () => ({}),
    } as DOMRect);

    await scrollViewport();

    expect(component.popoverStyle()['top']).toBe('84px');
    expect(component.isOpen()).toBe(true);
  });

  it('follows horizontally too, so a sideways scroll cannot strand it', async () => {
    placeTriggerAt(400);
    component.open();
    fixture.detectChanges();
    expect(component.popoverStyle()['left']).toBe('120px');

    (fixture.nativeElement.getBoundingClientRect as jasmine.Spy).and.returnValue({
      top: 400, bottom: 440, left: 20, right: 300, width: 280, height: 40, x: 20, y: 400,
      toJSON: () => ({}),
    } as DOMRect);

    await scrollViewport();

    expect(component.popoverStyle()['left']).toBe('20px');
  });

  /**
   * The other half of the same rule: once the trigger has scrolled out of sight the calendar must
   * not linger over content it has nothing to do with. It closes rather than following to nowhere.
   */
  it('closes when its trigger scrolls out of the viewport', async () => {
    placeTriggerAt(400);
    component.open();
    fixture.detectChanges();
    expect(component.isOpen()).toBe(true);

    (fixture.nativeElement.getBoundingClientRect as jasmine.Spy).and.returnValue({
      top: -200, bottom: -160, left: 120, right: 400, width: 280, height: 40, x: 120, y: -200,
      toJSON: () => ({}),
    } as DOMRect);

    await scrollViewport();

    expect(component.isOpen()).toBe(false);
  });

  it('stops listening once it is closed', async () => {
    placeTriggerAt(400);
    component.open();
    fixture.detectChanges();
    component.close();

    // A scroll after closing must not reopen or reposition anything.
    await scrollViewport();
    expect(component.isOpen()).toBe(false);
  });
});
