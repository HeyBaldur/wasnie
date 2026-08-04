import {
  Component,
  ElementRef,
  computed,
  effect,
  forwardRef,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * The result of one autosize pass.
 *
 * Extracted as a pure function below so the growth rule can be tested on its own, without depending
 * on a rendered element. (Headless Chrome DOES lay out once the element is attached to the document —
 * the collapse-on-clear test measures real heights — but the rule itself is worth pinning directly.)
 */
export interface WsTextareaAutosize {
  /** The height to apply, in px. Never below `minHeight`, never above `maxHeight`. */
  height: number;
  /** True once the content no longer fits: the element scrolls internally instead of growing. */
  scrollable: boolean;
}

/**
 * The growth rule, as a pure function: grow with the content, stop at the ceiling, then scroll.
 *
 * @param contentHeight the element's scrollHeight — how tall the content wants to be
 */
export function computeAutosize(
  contentHeight: number,
  minHeight: number,
  maxHeight: number,
): WsTextareaAutosize {
  // A max below the min would be a contradiction; the min wins, because a control smaller than one
  // line is unusable while one slightly taller than intended is merely wrong.
  const ceiling = Math.max(minHeight, maxHeight);
  const wanted = Math.max(minHeight, contentHeight);

  return {
    height: Math.min(wanted, ceiling),
    scrollable: wanted > ceiling,
  };
}

/**
 * Multi-line text control.
 *
 * ★ WHY IT EXISTS: `WsInput` is single-line by construction (its `type` is text|email|password|
 * number|search). Anything that takes a paragraph — a chat message, a note, a description, a pasted
 * stack trace — had no primitive, and the assistant's composer shipped as one line because of it.
 * This is a design-system gap, not a chat problem, so it is a primitive rather than a local widget.
 *
 * ★ TOKEN PARITY WITH WsInput IS THE POINT. The chrome (surface, border, radius, focus, error,
 * disabled) is expressed with the SAME token names and the SAME state-class contract as
 * `ws-input.component.scss` — `--color-bg-surface`, `--color-border-default`, `--color-border-focus` +
 * `--shadow-focus`, `--color-danger`, and the `--error` / `--disabled` / `--focused` modifiers. Note
 * for anyone extending this: the focus treatment in this design system is NOT a Tailwind ring; it is
 * `border-color: var(--color-border-focus)` plus `box-shadow: var(--shadow-focus)`. Writing a ring
 * utility here would look almost right and be wrong.
 *
 * ★ KEYBOARD: `submitOnEnter` (default true) gives the conversational contract — Enter sends,
 * Shift+Enter breaks the line. Set it to false for an ordinary textarea, where Enter just breaks the
 * line and nothing is submitted. The default favours the conversational case because that is the one
 * where getting it wrong is destructive: a chat where Enter inserts a newline silently swallows every
 * message the user thought they sent.
 */
@Component({
  selector: 'ws-textarea',
  standalone: true,
  imports: [TranslatePipe],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => WsTextareaComponent),
      multi: true,
    },
  ],
  templateUrl: './ws-textarea.component.html',
  styleUrl: './ws-textarea.component.scss',
})
export class WsTextareaComponent implements ControlValueAccessor {
  // ── Inputs mirrored from WsInput, so the two are interchangeable in a form ──
  readonly placeholder = input('');
  readonly error = input('');
  readonly label = input('');
  readonly inputId = input('');
  readonly maxlength = input<number | null>(null);

  // ── Multi-line specifics ──────────────────────────────────────────────────

  /** Enter submits and Shift+Enter breaks the line. False = ordinary textarea. */
  readonly submitOnEnter = input(true);

  /** Height floor, in px. Two lines' worth by default: enough to read as "you may write more". */
  readonly minHeight = input(56);

  /** Height ceiling, in px. Past it the control scrolls internally instead of growing. */
  readonly maxHeight = input(200);

  readonly valueChange = output<string>();

  /** Fires on Enter when `submitOnEnter` is on. Carries the current value for convenience. */
  readonly submitted = output<string>();

  readonly value = signal('');
  readonly isDisabled = signal(false);
  readonly isFocused = signal(false);

  /** True once the content hit `maxHeight`. Exposed so a consumer (and a test) can observe it. */
  readonly isScrollable = signal(false);

  readonly height = signal(0);

  private readonly textarea = viewChild<ElementRef<HTMLTextAreaElement>>('native');

  readonly hostClasses = computed(() =>
    [
      'ws-textarea',
      this.error() ? 'ws-textarea--error' : null,
      this.isDisabled() ? 'ws-textarea--disabled' : null,
    ]
      .filter(Boolean)
      .join(' '),
  );

  constructor() {
    // Re-measures when the value changes from OUTSIDE too (a form patch, a reset after send), not
    // only while typing — otherwise clearing the box leaves it stretched to the old paragraph.
    effect(() => {
      this.value();
      this.resize();
    });
  }

  private onChange: (v: string) => void = () => {};
  private onTouched: () => void = () => {};

  onInput(event: Event): void {
    const val = (event.target as HTMLTextAreaElement).value;
    this.value.set(val);
    this.onChange(val);
    this.valueChange.emit(val);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter' || !this.submitOnEnter()) {
      return;
    }

    // Shift+Enter is the line break, so it must reach the textarea untouched.
    if (event.shiftKey) {
      return;
    }

    // Mid-composition Enter belongs to the IME (picking a candidate in Japanese, Chinese, Korean),
    // not to us — submitting here would cut the word the user is still spelling.
    if (event.isComposing) {
      return;
    }

    event.preventDefault();
    this.submitted.emit(this.value());
  }

  onFocus(): void {
    this.isFocused.set(true);
  }

  onBlur(): void {
    this.isFocused.set(false);
    this.onTouched();
  }

  /**
   * Measure, then apply.
   *
   * Two things are load-bearing here, and both were bugs first:
   *
   * 1. The height is reset to `auto` before measuring, because `scrollHeight` of an element with an
   *    explicit height is that height — without the reset the box could grow but never shrink.
   *
   * 2. ★ The DOM value is synced from the model FIRST. This runs from an effect, which fires before
   *    the template has written the new value into the textarea, so measuring straight away measures
   *    the PREVIOUS content — the height lands one step behind. The visible symptom was a chat
   *    composer that stayed as tall as the paragraph that had just been sent, then grew when the next
   *    one was typed. Reading the model instead of the stale DOM removes the ordering question
   *    entirely rather than trying to schedule around it.
   *
   * The assignment is guarded on inequality: writing an identical string back would be a no-op for
   * the content but can disturb the caret while someone is typing.
   */
  resize(): void {
    const el = this.textarea()?.nativeElement;
    if (!el) {
      return;
    }

    if (el.value !== this.value()) {
      el.value = this.value();
    }

    el.style.height = 'auto';
    const result = computeAutosize(el.scrollHeight, this.minHeight(), this.maxHeight());

    el.style.height = `${result.height}px`;
    this.height.set(result.height);
    this.isScrollable.set(result.scrollable);
  }

  // ── ControlValueAccessor — identical contract to WsInput ───────────────────

  writeValue(val: string): void {
    this.value.set(val ?? '');
  }

  registerOnChange(fn: (v: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.isDisabled.set(disabled);
  }
}
