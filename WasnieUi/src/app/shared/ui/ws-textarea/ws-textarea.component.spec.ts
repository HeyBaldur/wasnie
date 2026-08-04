import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { WsTextareaComponent, computeAutosize } from './ws-textarea.component';
import { WsInputComponent } from '../ws-input/ws-input.component';

/**
 * The multi-line control.
 *
 * Two things are load-bearing and tested as such: the keyboard contract (Enter sends, Shift+Enter
 * breaks the line — get it backwards and a chat silently eats every message) and token parity with
 * WsInput (a control that looks nearly like the others is worse than one that clearly does not).
 */
describe('WsTextareaComponent', () => {
  let fixture: ComponentFixture<WsTextareaComponent>;
  let component: WsTextareaComponent;

  function textarea(): HTMLTextAreaElement {
    return fixture.nativeElement.querySelector('textarea');
  }

  function pressEnter(options: { shift?: boolean; composing?: boolean } = {}): KeyboardEvent {
    const event = new KeyboardEvent('keydown', {
      key: 'Enter',
      shiftKey: options.shift ?? false,
      bubbles: true,
      cancelable: true,
    });
    if (options.composing) {
      // isComposing is read-only on the event; defined here so the IME branch is reachable.
      Object.defineProperty(event, 'isComposing', { value: true });
    }
    textarea().dispatchEvent(event);
    return event;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WsTextareaComponent, TranslateModule.forRoot()],
    }).compileComponents();

    fixture = TestBed.createComponent(WsTextareaComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  // ── 1. ★ The keyboard contract ────────────────────────────────────────────

  describe('keyboard, with submitOnEnter on (the default)', () => {
    it('★ Enter submits and does NOT reach the textarea', () => {
      const submitted: string[] = [];
      component.submitted.subscribe((v) => submitted.push(v));
      component.writeValue('a question');
      fixture.detectChanges();

      const event = pressEnter();

      expect(submitted).toEqual(['a question']);
      // preventDefault is what stops Enter from ALSO inserting a newline before the box is cleared.
      expect(event.defaultPrevented).toBeTrue();
    });

    it('★ Shift+Enter does NOT submit and is left alone so it inserts a line break', () => {
      const submitted: string[] = [];
      component.submitted.subscribe((v) => submitted.push(v));

      const event = pressEnter({ shift: true });

      expect(submitted).toEqual([]);
      expect(event.defaultPrevented).withContext('the textarea must receive the newline').toBeFalse();
    });

    it('does not submit mid-IME-composition', () => {
      // Enter while composing picks a candidate in Japanese/Chinese/Korean input. Submitting there
      // would cut the word the user is still spelling.
      const submitted: string[] = [];
      component.submitted.subscribe((v) => submitted.push(v));

      const event = pressEnter({ composing: true });

      expect(submitted).toEqual([]);
      expect(event.defaultPrevented).toBeFalse();
    });

    it('ignores keys that are not Enter', () => {
      const submitted: string[] = [];
      component.submitted.subscribe((v) => submitted.push(v));

      textarea().dispatchEvent(new KeyboardEvent('keydown', { key: 'a', bubbles: true, cancelable: true }));

      expect(submitted).toEqual([]);
    });
  });

  // ── 2. The ordinary-textarea mode ─────────────────────────────────────────

  it('with submitOnEnter off, Enter inserts a line break and submits nothing', async () => {
    // The mode a notes or description field will want. Same primitive, different contract.
    await TestBed.resetTestingModule().configureTestingModule({
      imports: [WsTextareaComponent, TranslateModule.forRoot()],
    }).compileComponents();

    fixture = TestBed.createComponent(WsTextareaComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('submitOnEnter', false);
    fixture.detectChanges();

    const submitted: string[] = [];
    component.submitted.subscribe((v) => submitted.push(v));

    const event = pressEnter();

    expect(submitted).toEqual([]);
    expect(event.defaultPrevented).withContext('Enter belongs to the textarea in this mode').toBeFalse();
  });

  // ── 3. Autosize — the rule, not the pixels ────────────────────────────────

  describe('autosize', () => {
    // The growth RULE is pinned directly on the pure function; the wiring and the real measured
    // behaviour are covered separately below. Headless Chrome does lay out, but only for elements
    // attached to the document — a detached fixture reports scrollHeight 0.

    it('grows with the content once past the floor', () => {
      expect(computeAutosize(120, 56, 200)).toEqual({ height: 120, scrollable: false });
    });

    it('never shrinks below the floor, however little content there is', () => {
      expect(computeAutosize(18, 56, 200)).toEqual({ height: 56, scrollable: false });
      expect(computeAutosize(0, 56, 200)).toEqual({ height: 56, scrollable: false });
    });

    it('★ stops at the ceiling and switches to scrolling instead of growing', () => {
      expect(computeAutosize(340, 56, 200)).toEqual({ height: 200, scrollable: true });
    });

    it('treats exactly-at-the-ceiling as not yet scrollable', () => {
      // The boundary: content that fits perfectly must not show a scrollbar for nothing.
      expect(computeAutosize(200, 56, 200)).toEqual({ height: 200, scrollable: false });
    });

    it('lets the floor win over a contradictory ceiling', () => {
      // A control shorter than one line is unusable; one slightly taller than asked is merely wrong.
      expect(computeAutosize(80, 56, 20)).toEqual({ height: 56, scrollable: true });
    });

    it('is wired: the scrollable state drives the overflow class on the native element', () => {
      component.isScrollable.set(true);
      fixture.detectChanges();
      expect(textarea().classList).toContain('ws-textarea__native--scrollable');

      component.isScrollable.set(false);
      fixture.detectChanges();
      expect(textarea().classList).not.toContain('ws-textarea__native--scrollable');
    });

    it('is wired: resize() applies a height and records it', () => {
      component.resize();
      expect(component.height()).toBeGreaterThanOrEqual(component.minHeight());
      expect(textarea().style.height).toBe(`${component.height()}px`);
    });


    it('★ collapses back to the floor when the value is cleared from outside', () => {
      // ★ THE BUG THIS PINS: after a chat message is sent the box was staying as tall as the
      // paragraph that had just left it. Growing is only half of autosize — shrinking is the half a
      // user notices, because the stretched box eats the conversation above it on every send.
      // Attached to the document because a detached element has no layout, so scrollHeight is 0.
      document.body.appendChild(fixture.nativeElement);

      component.writeValue(['one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight'].join('\n'));
      fixture.detectChanges();
      const grown = component.height();

      component.writeValue('');
      fixture.detectChanges();
      const collapsed = component.height();

      document.body.removeChild(fixture.nativeElement);

      expect(grown).withContext('eight lines must have grown the box').toBeGreaterThan(component.minHeight());
      expect(collapsed).withContext('clearing must return it to the floor').toBe(component.minHeight());
    });

    it('re-measures when the value is replaced from outside, not only while typing', () => {
      // Clearing the box after a send must let it collapse; without this it stays paragraph-tall.
      const spy = spyOn(component, 'resize').and.callThrough();
      component.writeValue('something long');
      fixture.detectChanges();
      expect(spy).toHaveBeenCalled();
    });
  });

  // ── 4. ★ Parity with WsInput ──────────────────────────────────────────────

  describe('★ parity with WsInput', () => {
    it('applies the same state-class contract', () => {
      const inputFixture = TestBed.createComponent(WsInputComponent);
      inputFixture.detectChanges();

      const suffixes = (root: HTMLElement, base: string) => ({
        error: root.querySelector(`.${base}--error`) !== null,
        disabled: root.querySelector(`.${base}--disabled`) !== null,
        focused: root.querySelector(`.${base}__field--focused`) !== null,
      });

      // Focused + error + disabled, on both, expressed with the same modifier names.
      fixture.componentRef.setInput('error', 'VALIDATION.REQUIRED');
      component.setDisabledState(true);
      component.onFocus();
      fixture.detectChanges();

      inputFixture.componentRef.setInput('error', 'VALIDATION.REQUIRED');
      inputFixture.componentInstance.setDisabledState(true);
      inputFixture.componentInstance.onFocus();
      inputFixture.detectChanges();

      expect(suffixes(fixture.nativeElement, 'ws-textarea'))
        .toEqual(suffixes(inputFixture.nativeElement, 'ws-input'));
    });

    it('★ resolves to the SAME border and background as WsInput in the resting state', () => {
      // The real parity assertion: computed style, not a class name. If someone hand-writes a colour
      // or reaches for a Tailwind ring instead of --color-border-default / --shadow-focus, these two
      // stop matching and this test says so.
      const inputFixture = TestBed.createComponent(WsInputComponent);
      inputFixture.detectChanges();

      document.body.appendChild(fixture.nativeElement);
      document.body.appendChild(inputFixture.nativeElement);

      const mine = getComputedStyle(fixture.nativeElement.querySelector('.ws-textarea__field'));
      const theirs = getComputedStyle(inputFixture.nativeElement.querySelector('.ws-input__field'));

      expect(mine.borderTopColor).toBe(theirs.borderTopColor);
      expect(mine.borderTopWidth).toBe(theirs.borderTopWidth);
      expect(mine.borderTopStyle).toBe(theirs.borderTopStyle);
      expect(mine.borderRadius).toBe(theirs.borderRadius);
      expect(mine.backgroundColor).toBe(theirs.backgroundColor);

      document.body.removeChild(fixture.nativeElement);
      document.body.removeChild(inputFixture.nativeElement);
    });

    it('★ resolves to the SAME focus treatment as WsInput', () => {
      const inputFixture = TestBed.createComponent(WsInputComponent);
      inputFixture.detectChanges();

      component.onFocus();
      inputFixture.componentInstance.onFocus();
      fixture.detectChanges();
      inputFixture.detectChanges();

      document.body.appendChild(fixture.nativeElement);
      document.body.appendChild(inputFixture.nativeElement);

      const mine = getComputedStyle(fixture.nativeElement.querySelector('.ws-textarea__field'));
      const theirs = getComputedStyle(inputFixture.nativeElement.querySelector('.ws-input__field'));

      expect(mine.borderTopColor).toBe(theirs.borderTopColor);
      // The design system's focus is a box-shadow, NOT a Tailwind ring — same shadow on both.
      expect(mine.boxShadow).toBe(theirs.boxShadow);

      document.body.removeChild(fixture.nativeElement);
      document.body.removeChild(inputFixture.nativeElement);
    });

    it('implements the same ControlValueAccessor contract', () => {
      const seen: string[] = [];
      component.registerOnChange((v) => seen.push(v));

      component.writeValue('from the form');
      expect(component.value()).toBe('from the form');

      component.setDisabledState(true);
      expect(component.isDisabled()).toBeTrue();

      textarea().value = 'typed';
      textarea().dispatchEvent(new Event('input'));
      expect(seen).toEqual(['typed']);

      // writeValue(null) must not blow up — forms hand null on reset.
      component.writeValue(null as unknown as string);
      expect(component.value()).toBe('');
    });
  });
});

/**
 * The primitive inside a real form, which is the way every other Ws* control is consumed.
 */
@Component({
  standalone: true,
  // FormsModule goes in the standalone component's OWN imports — a TestBed provider does not
  // make ngModel available inside this template.
  imports: [WsTextareaComponent, FormsModule, TranslateModule],
  template: `<ws-textarea [(ngModel)]="text" (submitted)="sent.set($event)" />`,
})
class HostComponent {
  text = '';
  readonly sent = signal<string | null>(null);
}

describe('WsTextareaComponent — inside a form', () => {
  it('round-trips through ngModel and reports a submit', async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent, TranslateModule.forRoot()],
    }).compileComponents();

    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    const native: HTMLTextAreaElement = fixture.nativeElement.querySelector('textarea');
    native.value = 'a paragraph\nwith two lines';
    native.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.text).toBe('a paragraph\nwith two lines');

    native.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    expect(fixture.componentInstance.sent()).toBe('a paragraph\nwith two lines');
  });
});
