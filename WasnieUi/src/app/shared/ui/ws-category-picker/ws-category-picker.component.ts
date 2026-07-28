import { Component, computed, forwardRef, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  ControlValueAccessor,
  FormControl,
  NG_VALUE_ACCESSOR,
  ReactiveFormsModule,
} from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { WsInputComponent } from '../ws-input/ws-input.component';
import { WsSelectComponent, type SelectOption } from '../ws-select/ws-select.component';

/**
 * A single category value chosen from the tenant's known categories OR typed as a free value.
 *
 * Extracted from the rule-form's category condition so the SAME picker (choose from the real list,
 * escape hatch to a custom value, free-text fallback when the tenant has no categories yet) is used
 * both there and in the manual transaction form — one behaviour, no duplication.
 *
 * A ControlValueAccessor over a SINGLE string (null when empty). The In/NotIn multi-select and the
 * "value can never match" warning stay in the rule-form: those are rule-authoring concerns, not part
 * of picking one category for a transaction.
 */
@Component({
  selector: 'ws-category-picker',
  standalone: true,
  imports: [ReactiveFormsModule, TranslateModule, WsInputComponent, WsSelectComponent],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => WsCategoryPickerComponent),
      multi: true,
    },
  ],
  templateUrl: './ws-category-picker.component.html',
  styleUrl: './ws-category-picker.component.scss',
})
export class WsCategoryPickerComponent implements ControlValueAccessor {
  /** The tenant's known categories (from GET category-values). Empty → free-text fallback. */
  readonly options = input<string[]>([]);
  readonly label = input('');
  readonly placeholder = input('');
  /** Placeholder for the free-text ("use another value") input; falls back to `placeholder`. */
  readonly customPlaceholder = input('');

  /** value === label — category names are not i18n keys. */
  readonly selectOptions = computed<SelectOption[]>(() =>
    this.options().map(c => ({ value: c, label: c }))
  );

  readonly listEmpty = computed(() => this.options().length === 0);

  /** The current value, mirrored from the inner control so `autoCustom` can react to it. */
  private readonly _value = signal('');

  /** null until the user explicitly toggles; then their choice wins over the automatic mode. */
  private readonly _userCustom = signal<boolean | null>(null);

  readonly isDisabled = signal(false);

  /**
   * When the user has not chosen a mode: free-text if there are no categories, or if the current value
   * is not one of them (so a value from the CRM or an older rule stays VISIBLE and editable instead of
   * hiding behind an empty picker). Recomputes when the async options list finally lands.
   */
  private readonly _autoCustom = computed(() => {
    if (this.listEmpty()) return true;
    const v = this._value().trim();
    if (v.length === 0) return false;
    return !this.options().some(o => o.toLowerCase() === v.toLowerCase());
  });

  readonly useCustom = computed(() => this._userCustom() ?? this._autoCustom());

  readonly inner = new FormControl<string>('', { nonNullable: true });

  private _onChange: (v: string | null) => void = () => {};
  private _onTouched: () => void = () => {};

  constructor() {
    this.inner.valueChanges.pipe(takeUntilDestroyed()).subscribe(v => {
      this._value.set(v ?? '');
      const trimmed = (v ?? '').trim();
      this._onChange(trimmed.length > 0 ? v : null);
      this._onTouched();
    });
  }

  /** Escape hatch: flip between the category list and a free-typed value, explicitly. */
  toggleCustom(): void {
    this._userCustom.set(!this.useCustom());
  }

  // ── ControlValueAccessor ────────────────────────────────────────────────────────────────────
  writeValue(value: string | null): void {
    const v = value ?? '';
    this._value.set(v);
    this.inner.setValue(v, { emitEvent: false });
  }

  registerOnChange(fn: (v: string | null) => void): void {
    this._onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this._onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.isDisabled.set(isDisabled);
    if (isDisabled) this.inner.disable({ emitEvent: false });
    else this.inner.enable({ emitEvent: false });
  }
}
