import {
  Component,
  ElementRef,
  HostListener,
  ViewChild,
  computed,
  forwardRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';

export interface SelectOption {
  value: string | number;
  label: string;
  disabled?: boolean;
}

@Component({
  selector: 'ws-select',
  standalone: true,
  imports: [IconComponent, TranslatePipe],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => WsSelectComponent),
      multi: true,
    },
  ],
  templateUrl: './ws-select.component.html',
  styleUrl: './ws-select.component.scss',
})
export class WsSelectComponent implements ControlValueAccessor {
  @ViewChild('searchInput') searchInputRef!: ElementRef<HTMLInputElement>;

  readonly options = input.required<SelectOption[]>();
  readonly placeholder = input('');
  readonly searchable = input(false);
  readonly label = input('');
  readonly error = input('');

  readonly value = signal<string | number>('');
  readonly isOpen = signal(false);
  readonly isDisabled = signal(false);
  readonly isFocused = signal(false);
  readonly searchQuery = signal('');
  readonly activeIndex = signal(0);

  private readonly host = inject(ElementRef);

  readonly selectedOption = computed(() =>
    this.options().find(o => o.value === this.value()) ?? null
  );

  readonly filteredOptions = computed(() => {
    const q = this.searchQuery().toLowerCase();
    if (!q) return this.options();
    return this.options().filter(o => o.label.toLowerCase().includes(q));
  });

  readonly hostClasses = computed(() =>
    [
      'ws-select',
      this.error() ? 'ws-select--error' : null,
      this.isDisabled() ? 'ws-select--disabled' : null,
    ]
      .filter(Boolean)
      .join(' ')
  );

  private onChange: (v: string | number) => void = () => {};
  private onTouched: () => void = () => {};

  openDropdown(): void {
    if (this.isDisabled()) return;
    this.isOpen.set(true);
    this.searchQuery.set('');
    this.activeIndex.set(
      Math.max(0, this.filteredOptions().findIndex(o => o.value === this.value()))
    );
    setTimeout(() => this.searchInputRef?.nativeElement?.focus(), 10);
  }

  closeDropdown(): void {
    this.isOpen.set(false);
    this.onTouched();
  }

  toggleDropdown(): void {
    if (this.isOpen()) {
      this.closeDropdown();
    } else {
      this.openDropdown();
    }
  }

  select(option: SelectOption): void {
    if (option.disabled) return;
    this.value.set(option.value);
    this.onChange(option.value);
    this.closeDropdown();
  }

  onSearch(event: Event): void {
    this.searchQuery.set((event.target as HTMLInputElement).value);
    this.activeIndex.set(0);
  }

  onKeydown(event: KeyboardEvent): void {
    if (!this.isOpen()) {
      if (event.key === 'Enter' || event.key === ' ' || event.key === 'ArrowDown') {
        event.preventDefault();
        this.openDropdown();
      }
      return;
    }

    const opts = this.filteredOptions();
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.activeIndex.update(i => Math.min(i + 1, opts.length - 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.activeIndex.update(i => Math.max(i - 1, 0));
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const opt = opts[this.activeIndex()];
      if (opt) this.select(opt);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.closeDropdown();
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event): void {
    if (!this.isOpen()) return;
    if (!this.host.nativeElement.contains(event.target)) {
      this.closeDropdown();
    }
  }

  writeValue(val: string | number): void {
    this.value.set(val ?? '');
  }

  registerOnChange(fn: (v: string | number) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.isDisabled.set(disabled);
  }
}
