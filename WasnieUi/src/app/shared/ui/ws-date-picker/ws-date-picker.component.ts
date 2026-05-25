import {
  Component,
  HostListener,
  computed,
  forwardRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { TranslateService, TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';

const MONTH_KEYS = [
  'DATEPICKER.JANUARY', 'DATEPICKER.FEBRUARY', 'DATEPICKER.MARCH',
  'DATEPICKER.APRIL', 'DATEPICKER.MAY', 'DATEPICKER.JUNE',
  'DATEPICKER.JULY', 'DATEPICKER.AUGUST', 'DATEPICKER.SEPTEMBER',
  'DATEPICKER.OCTOBER', 'DATEPICKER.NOVEMBER', 'DATEPICKER.DECEMBER',
];

const DAY_KEYS = [
  'DATEPICKER.SUN', 'DATEPICKER.MON', 'DATEPICKER.TUE',
  'DATEPICKER.WED', 'DATEPICKER.THU', 'DATEPICKER.FRI', 'DATEPICKER.SAT',
];

export interface CalendarDay {
  date: Date | null;
  dayNum: number;
  isToday: boolean;
  isSelected: boolean;
  isOtherMonth: boolean;
  isDisabled: boolean;
}

@Component({
  selector: 'ws-date-picker',
  standalone: true,
  imports: [IconComponent, TranslatePipe],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => WsDatePickerComponent),
      multi: true,
    },
  ],
  templateUrl: './ws-date-picker.component.html',
  styleUrl: './ws-date-picker.component.scss',
})
export class WsDatePickerComponent implements ControlValueAccessor {
  private readonly translate = inject(TranslateService);

  readonly placeholder = input('');
  readonly label = input('');
  readonly error = input('');
  readonly minDate = input<string>('');
  readonly maxDate = input<string>('');

  readonly isOpen = signal(false);
  readonly isDisabled = signal(false);
  readonly viewYear = signal(new Date().getFullYear());
  readonly viewMonth = signal(new Date().getMonth());
  readonly selectedDate = signal<Date | null>(null);

  readonly dayKeys = DAY_KEYS;

  readonly monthLabel = computed(() =>
    this.translate.instant(MONTH_KEYS[this.viewMonth()])
  );

  readonly displayValue = computed(() => {
    const d = this.selectedDate();
    if (!d) return '';
    return new Intl.DateTimeFormat(this.translate.currentLang ?? 'en', {
      year: 'numeric', month: 'short', day: 'numeric',
    }).format(d);
  });

  readonly calendarDays = computed((): CalendarDay[] => {
    const year = this.viewYear();
    const month = this.viewMonth();
    const today = new Date();
    const selected = this.selectedDate();
    const firstDow = new Date(year, month, 1).getDay();
    const daysInMonth = new Date(year, month + 1, 0).getDate();
    const days: CalendarDay[] = [];

    for (let i = 0; i < firstDow; i++) {
      days.push({ date: null, dayNum: 0, isToday: false, isSelected: false, isOtherMonth: true, isDisabled: true });
    }

    for (let d = 1; d <= daysInMonth; d++) {
      const date = new Date(year, month, d);
      days.push({
        date,
        dayNum: d,
        isToday: this.isSameDay(date, today),
        isSelected: selected ? this.isSameDay(date, selected) : false,
        isOtherMonth: false,
        isDisabled: this.isDateDisabled(date),
      });
    }

    const remaining = (7 - (days.length % 7)) % 7;
    for (let i = 0; i < remaining; i++) {
      days.push({ date: null, dayNum: 0, isToday: false, isSelected: false, isOtherMonth: true, isDisabled: true });
    }

    return days;
  });

  private onChange: (v: string | null) => void = () => {};
  private onTouched: () => void = () => {};

  open(event: Event): void {
    if (this.isDisabled()) return;
    event.stopPropagation();
    this.isOpen.set(true);
    this.onTouched();
  }

  close(): void {
    this.isOpen.set(false);
  }

  selectDate(day: CalendarDay): void {
    if (!day.date || day.isOtherMonth || day.isDisabled) return;
    this.selectedDate.set(day.date);
    this.onChange(this.toIso(day.date));
    this.close();
  }

  selectToday(): void {
    const today = new Date();
    if (this.isDateDisabled(today)) return;
    this.selectedDate.set(today);
    this.viewYear.set(today.getFullYear());
    this.viewMonth.set(today.getMonth());
    this.onChange(this.toIso(today));
    this.close();
  }

  prevMonth(): void {
    if (this.viewMonth() === 0) {
      this.viewMonth.set(11);
      this.viewYear.update(y => y - 1);
    } else {
      this.viewMonth.update(m => m - 1);
    }
  }

  nextMonth(): void {
    if (this.viewMonth() === 11) {
      this.viewMonth.set(0);
      this.viewYear.update(y => y + 1);
    } else {
      this.viewMonth.update(m => m + 1);
    }
  }

  stopProp(e: Event): void {
    e.stopPropagation();
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.close();
  }

  writeValue(value: string | null): void {
    if (value) {
      const d = new Date(value + 'T00:00:00');
      if (!isNaN(d.getTime())) {
        this.selectedDate.set(d);
        this.viewYear.set(d.getFullYear());
        this.viewMonth.set(d.getMonth());
        return;
      }
    }
    this.selectedDate.set(null);
  }

  registerOnChange(fn: (v: string | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.isDisabled.set(disabled);
  }

  private isSameDay(a: Date, b: Date): boolean {
    return a.getFullYear() === b.getFullYear() &&
           a.getMonth() === b.getMonth() &&
           a.getDate() === b.getDate();
  }

  private toIso(d: Date): string {
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${d.getFullYear()}-${mm}-${dd}`;
  }

  private isDateDisabled(date: Date): boolean {
    const min = this.minDate();
    const max = this.maxDate();
    if (min) {
      const minD = new Date(min + 'T00:00:00');
      if (date < minD) return true;
    }
    if (max) {
      const maxD = new Date(max + 'T00:00:00');
      if (date > maxD) return true;
    }
    return false;
  }
}
