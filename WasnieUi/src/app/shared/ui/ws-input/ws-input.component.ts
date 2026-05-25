import { Component, computed, forwardRef, input, output, signal } from '@angular/core';
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
export class WsInputComponent implements ControlValueAccessor {
  readonly type = input<'text' | 'email' | 'password' | 'number' | 'search'>('text');
  readonly placeholder = input('');
  readonly prefixIcon = input('');
  readonly suffixText = input('');
  readonly clearable = input(false);
  readonly error = input('');
  readonly label = input('');
  readonly inputId = input('');
  readonly autocomplete = input('');

  readonly valueChange = output<string>();

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
