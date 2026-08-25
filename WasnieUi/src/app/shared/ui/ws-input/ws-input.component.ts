import {
  AfterViewInit,
  Component,
  ElementRef,
  computed,
  forwardRef,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';

@Component({
  selector: 'ws-input',
  standalone: true,
  imports: [IconComponent, TranslatePipe],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => WsInputComponent),
      multi: true,
    },
  ],
  templateUrl: './ws-input.component.html',
  styleUrl: './ws-input.component.scss',
})
export class WsInputComponent implements ControlValueAccessor, AfterViewInit {
  readonly type = input<'text' | 'email' | 'password' | 'number' | 'search'>('text');
  readonly placeholder = input('');
  readonly prefixIcon = input('');
  readonly suffixText = input('');
  readonly clearable = input(false);
  readonly error = input('');
  readonly label = input('');
  readonly inputId = input('');
  readonly autocomplete = input('');
  readonly maxlength = input<number | null>(null);

  /**
   * Take the caret as soon as the box appears.
   *
   * ★ FOR BOXES THAT APPEAR IN PLACE OF SOMETHING ELSE — an inline rename, a cell that becomes editable
   * — where the user has already said what they want to do and should not have to click again to say it
   * a second time. Do NOT set it on an ordinary form field: stealing focus on page load moves a screen
   * reader off the heading it was announcing and scrolls the page under a sighted user.
   *
   * ★ IT ALSO MAKES "CLICK AWAY TO CANCEL" POSSIBLE AT ALL. A box that never held focus can never lose
   * it, so no `blur`/`focusout` handler on it will ever fire — an inline editor wired that way stays on
   * screen forever, orphaned, with no way out but the keyboard. That is a real bug this fixed
   * (`/assistant` rename, 2026-08-18), not a hypothetical.
   */
  readonly autofocus = input(false);

  private readonly nativeInput = viewChild<ElementRef<HTMLInputElement>>('native');

  readonly valueChange = output<string>();

  ngAfterViewInit(): void {
    if (this.autofocus()) {
      this.focus();
    }
  }

  /**
   * Puts the caret in the box and selects what is already there.
   *
   * Selecting rather than appending: an inline editor is opened to REPLACE a name far more often than to
   * add to one, so the next keystroke should overwrite. The same shape as `WsTextarea.fill`.
   */
  focus(): void {
    const el = this.nativeInput()?.nativeElement;
    if (!el) {
      return;
    }

    el.focus();
    el.select();
  }

  readonly value = signal('');
  readonly isDisabled = signal(false);
  readonly isFocused = signal(false);
  readonly showPassword = signal(false);

  readonly inputType = computed(() =>
    this.type() === 'password' && this.showPassword() ? 'text' : this.type()
  );

  readonly hasSuffix = computed(
    () => !!this.suffixText() || (this.clearable() && !!this.value()) || this.type() === 'password'
  );

  readonly hostClasses = computed(() =>
    [
      'ws-input',
      this.error() ? 'ws-input--error' : null,
      this.isDisabled() ? 'ws-input--disabled' : null,
    ]
      .filter(Boolean)
      .join(' ')
  );

  private onChange: (v: string) => void = () => {};
  private onTouched: () => void = () => {};

  onInput(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.value.set(val);
    this.onChange(val);
    this.valueChange.emit(val);
  }

  onFocus(): void {
    this.isFocused.set(true);
  }

  onBlur(): void {
    this.isFocused.set(false);
    this.onTouched();
  }

  clear(): void {
    this.value.set('');
    this.onChange('');
    this.valueChange.emit('');
  }

  togglePassword(): void {
    this.showPassword.update(v => !v);
  }

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
