import {
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  ViewChild,
  computed,
  forwardRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { Observable, Subject, catchError, debounceTime, of, switchMap, tap } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
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
  @ViewChild('trigger') triggerRef!: ElementRef<HTMLElement>;

  // options is now optional — existing callers pass it unchanged; async callers omit it
  readonly options = input<SelectOption[]>([]);
  readonly placeholder = input('');
  readonly searchable = input(false);
  readonly label = input('');
  readonly error = input('');
  readonly noResultsLabel = input('COMMON.NO_RESULTS');

  // Async mode: consumer provides a function that turns a query string into an Observable<SelectOption[]>.
  // When non-null the component is in async mode; the [options] input is ignored.
  // The function must NOT inject HttpClient directly — it belongs to the consumer's service layer.
  readonly searchFn = input<((query: string) => Observable<SelectOption[]>) | null>(null);

  // Edit-mode resolution: when the form patches a value that is not in asyncOptions (e.g. an entity
  // loaded on a different server page), the consumer supplies the known {value, label} here so the
  // trigger never shows a blank or raw UUID.
  readonly initialOption = input<SelectOption | null>(null);

  readonly value = signal<string | number>('');
  readonly isOpen = signal(false);
  readonly isDisabled = signal(false);
  readonly isFocused = signal(false);
  readonly searchQuery = signal('');
  readonly activeIndex = signal(0);
  readonly dropdownUpward = signal(false);
  readonly constrainedListHeight = signal<number | null>(null);

  // Fixed positioning — used when ws-select is inside a modal (escapes overflow: hidden).
  readonly dropdownFixed = signal(false);
  readonly dropdownLeft = signal(0);
  readonly dropdownWidth = signal(0);
  readonly dropdownFixedTop = signal<number | null>(null);
  readonly dropdownFixedBottom = signal<number | null>(null);

  // Async state (only active when searchFn is non-null)
  readonly asyncOptions = signal<SelectOption[]>([]);
  readonly asyncLoading = signal(false);

  readonly listMaxHeight = computed(() => {
    const h = this.constrainedListHeight();
    if (h === null) return null;
    const overhead = (this.searchable() ? 44 : 0) + 24;
    return Math.max(60, h - overhead);
  });

  private readonly host = inject(ElementRef);
  private readonly _destroyRef = inject(DestroyRef);
  private readonly _translate = inject(TranslateService);
  private readonly _searchSubject$ = new Subject<string>();

  // In async mode, selectedOption checks asyncOptions first, then falls back to initialOption (edit
  // mode resolution). In client-side mode, behavior is identical to before.
  readonly selectedOption = computed(() => {
    const v = this.value();
    if (this.searchFn()) {
      return this.asyncOptions().find(o => o.value === v) ?? this.initialOption() ?? null;
    }
    return this.options().find(o => o.value === v) ?? null;
  });

  // In async mode, the server already filtered — return asyncOptions as-is (no client-side filter).
  // In client-side mode, filter against the TRANSLATED label, normalizing diacritics so "espana"
  // matches "España" and "pol" matches "Poland/Polonia/Polska" regardless of case or accent.
  readonly filteredOptions = computed(() => {
    if (this.searchFn()) return this.asyncOptions();
    const q = this._norm(this.searchQuery());
    if (!q) return this.options();
    return this.options().filter(o =>
      this._norm(this._translate.instant(String(o.label))).includes(q)
    );
  });

  private _norm(s: string): string {
    return s.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();
  }

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

  constructor() {
    // switchMap is REQUIRED here (not mergeMap/concatMap): it cancels the prior in-flight request
    // whenever a newer search fires, preventing a slow earlier response from overwriting a later one.
    this._searchSubject$.pipe(
      debounceTime(300),
      tap(() => this.asyncLoading.set(true)),
      switchMap(q => {
        const fn = this.searchFn();
        if (!fn) return of<SelectOption[]>([]);
        return fn(q).pipe(catchError(() => of<SelectOption[]>([])));
      }),
      takeUntilDestroyed(this._destroyRef),
    ).subscribe(options => {
      this.asyncOptions.set(options);
      this.asyncLoading.set(false);
    });
  }

  openDropdown(): void {
    if (this.isDisabled()) return;
    // Measure the trigger button specifically, not the host (which includes the label above).
    const triggerEl = (this.triggerRef?.nativeElement ?? this.host.nativeElement) as HTMLElement;
    const triggerRect = triggerEl.getBoundingClientRect();

    const containerBottom = window.innerHeight - 8;
    const containerTop = 8;

    // Async mode: options arrive after open, so always estimate the max height.
    const estimatedHeight = this.searchFn()
      ? 280
      : Math.min(280, this.filteredOptions().length * 36 + (this.searchable() ? 44 : 0) + 16);

    const spaceBelow = containerBottom - triggerRect.bottom;
    const spaceAbove = triggerRect.top - containerTop;

    if (spaceBelow >= estimatedHeight) {
      this.dropdownUpward.set(false);
      this.constrainedListHeight.set(null);
    } else if (spaceAbove >= estimatedHeight) {
      this.dropdownUpward.set(true);
      this.constrainedListHeight.set(null);
    } else {
      this.dropdownUpward.set(spaceAbove > spaceBelow);
      this.constrainedListHeight.set(Math.max(spaceBelow, spaceAbove) - 8);
    }

    // Always use position:fixed so the dropdown escapes any overflow-constrained ancestor
    // (filter panels, cards, page scroll containers) — same strategy as ws-date-picker.
    this.dropdownFixed.set(true);
    this.dropdownLeft.set(triggerRect.left);
    this.dropdownWidth.set(triggerRect.width);
    if (this.dropdownUpward()) {
      this.dropdownFixedBottom.set(window.innerHeight - triggerRect.top + 4);
      this.dropdownFixedTop.set(null);
    } else {
      this.dropdownFixedTop.set(triggerRect.bottom + 4);
      this.dropdownFixedBottom.set(null);
    }

    this.isOpen.set(true);
    this.searchQuery.set('');
    this.activeIndex.set(
      Math.max(0, this.filteredOptions().findIndex(o => o.value === this.value()))
    );
    setTimeout(() => this.searchInputRef?.nativeElement?.focus(), 10);

    // In async mode trigger an initial load (empty query = first server page) so the
    // dropdown is not blank before the user starts typing.
    if (this.searchFn()) {
      this._searchSubject$.next('');
    }
  }

  closeDropdown(): void {
    this.isOpen.set(false);
    this.dropdownFixed.set(false);
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
    const q = (event.target as HTMLInputElement).value;
    this.searchQuery.set(q);
    this.activeIndex.set(0);
    // In async mode push the query through the debounced Subject; client-side mode uses
    // filteredOptions computed directly and does not need the Subject.
    if (this.searchFn()) {
      this._searchSubject$.next(q);
    }
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

  @HostListener('window:scroll')
  onWindowScroll(): void {
    if (this.isOpen()) this.closeDropdown();
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    if (this.isOpen()) this.closeDropdown();
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
