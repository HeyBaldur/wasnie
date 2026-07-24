import {
  AfterViewInit,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  ViewChild,
  computed,
  forwardRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { TranslateService, TranslatePipe } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import {
  format,
  parse,
  parseISO,
  isValid,
  startOfMonth,
  getDay,
  getDaysInMonth,
  isToday,
  isSameDay,
  addDays,
  addMonths,
  getMonth,
  getYear,
  setYear as dfSetYear,
} from 'date-fns';
import { enUS, es, pl } from 'date-fns/locale';
import type { Locale } from 'date-fns';
import { IconComponent } from '../../components/icon/icon.component';
import { WsButtonComponent } from '../ws-button/ws-button.component';

// Kept for backward compatibility with ws-date-range-picker
export interface CalendarDay {
  date: Date | null;
  dayNum: number;
  isToday: boolean;
  isSelected: boolean;
  isOtherMonth: boolean;
  isDisabled: boolean;
}

export interface CalendarCell {
  date: Date | null;
  dayNum: number;
  isEmpty: boolean;
  isToday: boolean;
  isSelected: boolean;
  isDisabled: boolean;
  key: string;
}

const LOCALE_MAP: Record<string, Locale> = { en: enUS, es, pl };

@Component({
  selector: 'ws-date-picker',
  standalone: true,
  imports: [IconComponent, TranslatePipe, WsButtonComponent],
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => WsDatePickerComponent),
    multi: true,
  }],
  templateUrl: './ws-date-picker.component.html',
  styleUrl: './ws-date-picker.component.scss',
})
export class WsDatePickerComponent implements ControlValueAccessor, AfterViewInit, OnDestroy {
  @ViewChild('inputEl') inputElRef!: ElementRef<HTMLInputElement>;

  readonly label = input('');
  readonly error = input('');
  readonly minDate = input<string>('');
  readonly maxDate = input<string>('');

  readonly selectedDate = signal<Date | null>(null);
  readonly viewMonth = signal(new Date().getMonth());
  readonly viewYear = signal(new Date().getFullYear());
  readonly viewMode = signal<'date' | 'monthYear'>('date');
  readonly openUpward = signal(false);
  readonly isOpen = signal(false);
  readonly isDisabled = signal(false);
  readonly yearRangeStart = signal(Math.floor(new Date().getFullYear() / 12) * 12);
  readonly hasInputError = signal(false);

  // When inside a modal, the calendar uses position:fixed to escape overflow:hidden
  readonly useFixed = signal(false);
  private fixedLeft = 0;
  private fixedWidth = 0;
  private fixedVertical = 0; // top (below) or bottom (above) value in px

  readonly popoverStyle = computed((): Record<string, string> => {
    if (!this.useFixed()) return {};
    const base: Record<string, string> = {
      position: 'fixed',
      left: `${this.fixedLeft}px`,
      width: `${this.fixedWidth}px`,
      minWidth: '280px',
      zIndex: '1100',
    };
    if (this.openUpward()) {
      return { ...base, bottom: `${this.fixedVertical}px`, top: 'auto' };
    }
    return { ...base, top: `${this.fixedVertical}px`, bottom: 'auto' };
  });

  private readonly host = inject(ElementRef);
  private readonly translate = inject(TranslateService);
  private readonly dfLocale = signal<Locale>(this.getLangLocale());
  private langSub!: Subscription;
  private isTyping = false;

  private onChange: (v: string | null) => void = () => {};
  private onTouched: () => void = () => {};

  readonly displayValue = computed((): string => {
    const d = this.selectedDate();
    if (!d) return '';
    try { return format(d, 'PP', { locale: this.dfLocale() }); } catch { return ''; }
  });

  readonly monthYearLabel = computed((): string => {
    try {
      return format(new Date(this.viewYear(), this.viewMonth(), 1), 'MMMM yyyy', { locale: this.dfLocale() });
    } catch { return ''; }
  });

  readonly weekdayLabels = computed((): string[] => {
    const locale = this.dfLocale();
    const weekStart = locale.options?.weekStartsOn ?? 0;
    return Array.from({ length: 7 }, (_, i) => {
      const dayIdx = (weekStart + i) % 7;
      // 2024-01-07 is Sunday; dayIdx 0..6 → Jan 7..13
      const d = new Date(2024, 0, 7 + dayIdx);
      try { return format(d, 'EEEEEE', { locale }); } catch { return ''; }
    });
  });

  readonly calendarDays = computed((): CalendarCell[] => {
    const year = this.viewYear();
    const month = this.viewMonth();
    const selected = this.selectedDate();
    const locale = this.dfLocale();
    const weekStart = locale.options?.weekStartsOn ?? 0;
    const firstDay = startOfMonth(new Date(year, month));
    const daysInMonth = getDaysInMonth(firstDay);
    const leadingEmpty = (getDay(firstDay) - weekStart + 7) % 7;
    const cells: CalendarCell[] = [];

    for (let i = 0; i < leadingEmpty; i++) {
      cells.push({ date: null, dayNum: 0, isEmpty: true, isToday: false, isSelected: false, isDisabled: true, key: `es${i}` });
    }
    for (let d = 1; d <= daysInMonth; d++) {
      const date = new Date(year, month, d);
      cells.push({
        date, dayNum: d, isEmpty: false,
        isToday: isToday(date),
        isSelected: selected ? isSameDay(date, selected) : false,
        isDisabled: this.isDateDisabled(date),
        key: `${year}-${month}-${d}`,
      });
    }
    const remaining = (7 - (cells.length % 7)) % 7;
    for (let i = 0; i < remaining; i++) {
      cells.push({ date: null, dayNum: 0, isEmpty: true, isToday: false, isSelected: false, isDisabled: true, key: `ee${i}` });
    }
    return cells;
  });

  readonly yearGrid = computed((): number[] =>
    Array.from({ length: 12 }, (_, i) => this.yearRangeStart() + i)
  );

  readonly yearRangeLabel = computed((): string => {
    const s = this.yearRangeStart();
    return `${s} – ${s + 11}`;
  });

  readonly monthLabels = computed((): string[] => {
    const locale = this.dfLocale();
    return Array.from({ length: 12 }, (_, i) => {
      try { return format(new Date(2024, i, 1), 'MMM', { locale }); } catch { return ''; }
    });
  });

  constructor() {
    this.langSub = this.translate.onLangChange.subscribe(() => {
      this.dfLocale.set(this.getLangLocale());
      this.syncInput();
    });
  }

  ngAfterViewInit(): void {
    this.syncInput();
  }

  ngOnDestroy(): void {
    this.langSub?.unsubscribe();
    this._detachViewportListeners();
  }

  // --- Open / close ---

  toggle(): void {
    if (this.isDisabled()) return;
    if (this.isOpen()) this.close();
    else this.open();
  }

  open(): void {
    if (this.isDisabled()) return;
    this.computePlacement();
    this.viewMode.set('date');
    const sel = this.selectedDate();
    if (sel) {
      this.viewMonth.set(getMonth(sel));
      this.viewYear.set(getYear(sel));
    } else {
      const now = new Date();
      this.viewMonth.set(now.getMonth());
      this.viewYear.set(now.getFullYear());
    }
    this.isOpen.set(true);
    this._attachViewportListeners();
    this.onTouched();
  }

  close(): void {
    this.isOpen.set(false);
    this.viewMode.set('date');
    this._detachViewportListeners();
  }

  // The calendar is position:fixed, measured relative to the viewport, so any ancestor scroll moves the
  // trigger out from under it. The page scrolls inside app-shell's `overflow-y-auto` container (not
  // `window`), so a `window:scroll` listener would never fire — we listen on `window` in the CAPTURE
  // phase, which sees scrolls from every nested container, and REPOSITION so the calendar follows its
  // trigger (a normal dropdown stays open while scrolling; only an outside click closes it). A scroll
  // INSIDE the calendar itself is ignored. resize = same. Throttled to one rAF to avoid layout thrash.
  private _repositionScheduled = false;
  private readonly _onViewportScroll = (e: Event): void => {
    if (!this.isOpen()) return;
    if ((this.host.nativeElement as HTMLElement).contains(e.target as Node)) return;
    this._scheduleReposition();
  };
  private readonly _onViewportResize = (): void => {
    if (this.isOpen()) this._scheduleReposition();
  };

  private _scheduleReposition(): void {
    if (this._repositionScheduled) return;
    this._repositionScheduled = true;
    requestAnimationFrame(() => {
      this._repositionScheduled = false;
      if (this.isOpen()) this.computePlacement();
    });
  }

  private _attachViewportListeners(): void {
    window.addEventListener('scroll', this._onViewportScroll, { capture: true, passive: true });
    window.addEventListener('resize', this._onViewportResize, { passive: true });
  }

  private _detachViewportListeners(): void {
    window.removeEventListener('scroll', this._onViewportScroll, { capture: true } as EventListenerOptions);
    window.removeEventListener('resize', this._onViewportResize);
  }

  private computePlacement(): void {
    const el = this.host.nativeElement as HTMLElement;
    const rect = el.getBoundingClientRect();
    // If the trigger has scrolled out of the viewport, the calendar must not linger over other content.
    if (this.isOpen() && (rect.bottom < 0 || rect.top > window.innerHeight)) {
      this.close();
      return;
    }
    const spaceBelow = window.innerHeight - 8 - rect.bottom;
    const spaceAbove = rect.top - 8;
    const goUpward = spaceBelow < 350 && spaceAbove > spaceBelow;
    this.useFixed.set(true);
    this.openUpward.set(goUpward);
    this.fixedLeft = rect.left;
    this.fixedWidth = Math.max(rect.width, 280);
    this.fixedVertical = goUpward
      ? window.innerHeight - rect.top + 4
      : rect.bottom + 4;
  }

  // --- Navigation ---

  prevMonth(): void {
    if (this.viewMonth() === 0) { this.viewMonth.set(11); this.viewYear.update(y => y - 1); }
    else this.viewMonth.update(m => m - 1);
  }

  nextMonth(): void {
    if (this.viewMonth() === 11) { this.viewMonth.set(0); this.viewYear.update(y => y + 1); }
    else this.viewMonth.update(m => m + 1);
  }

  prevYearRange(): void { this.yearRangeStart.update(s => s - 12); }
  nextYearRange(): void { this.yearRangeStart.update(s => s + 12); }

  switchToMonthYear(): void {
    this.yearRangeStart.set(Math.floor(this.viewYear() / 12) * 12);
    this.viewMode.set('monthYear');
  }

  switchToDate(): void { this.viewMode.set('date'); }

  selectYear(year: number): void { this.viewYear.set(year); }

  selectMonth(idx: number): void {
    this.viewMonth.set(idx);
    this.viewMode.set('date');
  }

  // --- Date selection ---

  onDayClick(cell: CalendarCell): void {
    if (!cell.date || cell.isEmpty || cell.isDisabled) return;
    this.setDate(cell.date);
    this.close();
  }

  selectToday(): void {
    const today = new Date();
    if (this.isDateDisabled(today)) return;
    this.setDate(today);
    this.viewMonth.set(today.getMonth());
    this.viewYear.set(today.getFullYear());
    this.close();
  }

  clearDate(): void {
    this.selectedDate.set(null);
    this.onChange(null);
    this.syncInput();
    this.close();
  }

  private setDate(date: Date): void {
    this.selectedDate.set(date);
    this.onChange(format(date, 'yyyy-MM-dd'));
    this.syncInput();
  }

  // --- Trigger interaction ---

  onTriggerClick(): void {
    if (this.isDisabled()) return;
    if (this.isTyping) {
      this.parseAndValidate();
      this.isTyping = false;
    }
    this.toggle();
  }

  onUserTyping(): void {
    this.isTyping = true;
    if (this.isOpen()) this.close();
  }

  onInputKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.close();
      this.syncInput();
      this.isTyping = false;
      return;
    }
    if (event.key === 'Enter') {
      event.preventDefault();
      if (!this.isTyping && this.isOpen()) { this.close(); return; }
      this.parseAndValidate();
      this.isTyping = false;
      this.close();
      return;
    }
    if (this.isOpen() && !this.isTyping) {
      this.onGridKeydown(event);
    }
  }

  onInputBlur(): void {
    setTimeout(() => {
      if (this.isOpen()) return;
      const el = this.inputElRef?.nativeElement;
      if (!el) return;
      if (el.value === this.displayValue()) { this.isTyping = false; return; }
      this.parseAndValidate();
      this.isTyping = false;
    }, 200);
  }

  private parseAndValidate(): void {
    const el = this.inputElRef?.nativeElement;
    if (!el) return;
    const raw = el.value.trim();
    if (!raw) { this.clearDate(); return; }
    const locale = this.dfLocale();
    let parsed: Date | null = null;
    // ISO format (yyyy-MM-dd)
    if (!parsed) { try { const d = new Date(raw + 'T00:00:00'); if (!isNaN(d.getTime())) parsed = d; } catch {} }
    // Locale long format (e.g. "May 12, 2026")
    if (!parsed) { try { const d = parse(raw, 'PP', new Date(), { locale }); if (isValid(d)) parsed = d; } catch {} }
    // Locale short format (e.g. "5/12/2026")
    if (!parsed) { try { const d = parse(raw, 'P', new Date(), { locale }); if (isValid(d)) parsed = d; } catch {} }
    if (parsed && !this.isDateDisabled(parsed)) {
      this.setDate(parsed);
    } else {
      this.syncInput();
      this.hasInputError.set(true);
      setTimeout(() => this.hasInputError.set(false), 1500);
    }
  }

  private onGridKeydown(event: KeyboardEvent): void {
    const current = this.selectedDate() ?? new Date(this.viewYear(), this.viewMonth(), 1);
    let next: Date | null = null;
    switch (event.key) {
      case 'ArrowLeft': event.preventDefault(); next = addDays(current, -1); break;
      case 'ArrowRight': event.preventDefault(); next = addDays(current, 1); break;
      case 'ArrowUp': event.preventDefault(); next = addDays(current, -7); break;
      case 'ArrowDown': event.preventDefault(); next = addDays(current, 7); break;
      case 'PageUp':
        event.preventDefault();
        next = event.shiftKey ? dfSetYear(current, getYear(current) - 1) : addMonths(current, -1);
        break;
      case 'PageDown':
        event.preventDefault();
        next = event.shiftKey ? dfSetYear(current, getYear(current) + 1) : addMonths(current, 1);
        break;
    }
    if (next && !this.isDateDisabled(next)) {
      this.viewMonth.set(getMonth(next));
      this.viewYear.set(getYear(next));
      this.selectedDate.set(next);
      this.onChange(format(next, 'yyyy-MM-dd'));
    }
  }

  stopProp(e: Event): void { e.stopPropagation(); }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event): void {
    if (!this.isOpen()) return;
    if (!(this.host.nativeElement as HTMLElement).contains(event.target as Node)) this.close();
  }

  // --- CVA ---

  writeValue(value: string | null): void {
    if (value) {
      const d = new Date(value.includes('T') ? value : value + 'T00:00:00');
      if (!isNaN(d.getTime())) {
        this.selectedDate.set(d);
        this.viewMonth.set(getMonth(d));
        this.viewYear.set(getYear(d));
        this.syncInput();
        return;
      }
    }
    this.selectedDate.set(null);
    this.syncInput();
  }

  registerOnChange(fn: (v: string | null) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(disabled: boolean): void { this.isDisabled.set(disabled); }

  // --- Helpers ---

  private syncInput(): void {
    if (this.inputElRef?.nativeElement) {
      this.inputElRef.nativeElement.value = this.displayValue();
    }
  }

  private getLangLocale(): Locale {
    return LOCALE_MAP[this.translate.currentLang ?? 'en'] ?? enUS;
  }

  private isDateDisabled(date: Date): boolean {
    const dateStr = format(date, 'yyyy-MM-dd');
    const min = this.minDate();
    const max = this.maxDate();
    if (min && dateStr < min) return true;
    if (max && dateStr > max) return true;
    return false;
  }
}
