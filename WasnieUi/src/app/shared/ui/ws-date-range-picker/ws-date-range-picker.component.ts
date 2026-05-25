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
import { CalendarDay } from '../ws-date-picker/ws-date-picker.component';

export interface DateRange {
  start: string | null;
  end: string | null;
}

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

@Component({
  selector: 'ws-date-range-picker',
  standalone: true,
  imports: [IconComponent, TranslatePipe],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => WsDateRangePickerComponent),
      multi: true,
    },
  ],
  templateUrl: './ws-date-range-picker.component.html',
  styleUrl: './ws-date-range-picker.component.scss',
})
export class WsDateRangePickerComponent implements ControlValueAccessor {
  private readonly translate = inject(TranslateService);

  readonly placeholder = input('DATEPICKER.SELECT_DATE');
  readonly label = input('');
  readonly error = input('');

  readonly isOpen = signal(false);
  readonly isDisabled = signal(false);

  readonly leftYear = signal(new Date().getFullYear());
  readonly leftMonth = signal(new Date().getMonth());
  readonly startDate = signal<Date | null>(null);
  readonly endDate = signal<Date | null>(null);
  readonly hoverDate = signal<Date | null>(null);
  readonly selectingEnd = signal(false);

  readonly dayKeys = DAY_KEYS;

  readonly leftMonthLabel = computed(() =>
    this.translate.instant(MONTH_KEYS[this.leftMonth()])
  );

  readonly rightYear = computed(() =>
    this.leftMonth() === 11 ? this.leftYear() + 1 : this.leftYear()
  );

  readonly rightMonth = computed(() =>
    this.leftMonth() === 11 ? 0 : this.leftMonth() + 1
  );

  readonly rightMonthLabel = computed(() =>
    this.translate.instant(MONTH_KEYS[this.rightMonth()])
  );

  readonly displayValue = computed(() => {
    const s = this.startDate();
    const e = this.endDate();
    const fmt = (d: Date) =>
      new Intl.DateTimeFormat(this.translate.currentLang ?? 'en', {
        month: 'short', day: 'numeric', year: 'numeric',
      }).format(d);
    if (s && e) return `${fmt(s)} – ${fmt(e)}`;
    if (s) return fmt(s);
    return '';
  });

  readonly leftDays = computed(() => this.buildMonth(this.leftYear(), this.leftMonth()));
  readonly rightDays = computed(() => this.buildMonth(this.rightYear(), this.rightMonth()));

  private onChange: (v: DateRange) => void = () => {};
  private onTouched: () => void = () => {};

  open(event: Event): void {
    if (this.isDisabled()) return;
    event.stopPropagation();
    this.isOpen.set(true);
    this.selectingEnd.set(false);
    this.onTouched();
  }

  close(): void {
    this.isOpen.set(false);
  }

  stopProp(e: Event): void {
    e.stopPropagation();
  }

  prevMonth(): void {
    if (this.leftMonth() === 0) {
      this.leftMonth.set(11);
      this.leftYear.update(y => y - 1);
    } else {
      this.leftMonth.update(m => m - 1);
    }
  }

  nextMonth(): void {
    if (this.leftMonth() === 11) {
      this.leftMonth.set(0);
      this.leftYear.update(y => y + 1);
    } else {
      this.leftMonth.update(m => m + 1);
    }
  }

  onCellHover(day: CalendarDay): void {
    if (this.selectingEnd() && day.date) {
      this.hoverDate.set(day.date);
    }
  }

  selectDate(day: CalendarDay): void {
    if (!day.date || day.isOtherMonth) return;
    if (!this.selectingEnd()) {
      this.startDate.set(day.date);
      this.endDate.set(null);
      this.selectingEnd.set(true);
    } else {
      let start = this.startDate()!;
      let end = day.date;
      if (end < start) [start, end] = [end, start];
      this.startDate.set(start);
      this.endDate.set(end);
      this.hoverDate.set(null);
      this.selectingEnd.set(false);
      this.onChange({ start: this.toIso(start), end: this.toIso(end) });
      this.close();
    }
  }

  isInRange(date: Date | null): boolean {
    if (!date) return false;
    const start = this.startDate();
    const end = this.endDate() ?? this.hoverDate();
    if (!start || !end) return false;
    const lo = start < end ? start : end;
    const hi = start < end ? end : start;
    return date > lo && date < hi;
  }

  isStart(date: Date | null): boolean {
    const s = this.startDate();
    return !!date && !!s && this.isSameDay(date, s);
  }

  isEnd(date: Date | null): boolean {
    const e = this.endDate();
    return !!date && !!e && this.isSameDay(date, e);
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.close();
  }

  writeValue(val: DateRange | null): void {
    if (val?.start) {
      const s = new Date(val.start + 'T00:00:00');
      if (!isNaN(s.getTime())) {
        this.startDate.set(s);
        this.leftYear.set(s.getFullYear());
        this.leftMonth.set(s.getMonth());
      }
    } else {
      this.startDate.set(null);
    }
    if (val?.end) {
      const e = new Date(val.end + 'T00:00:00');
      if (!isNaN(e.getTime())) this.endDate.set(e);
    } else {
      this.endDate.set(null);
    }
  }

  registerOnChange(fn: (v: DateRange) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.isDisabled.set(disabled);
  }

  private buildMonth(year: number, month: number): CalendarDay[] {
    const today = new Date();
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
        isSelected: this.isStart(date) || this.isEnd(date),
        isOtherMonth: false,
        isDisabled: false,
      });
    }

    const remaining = (7 - (days.length % 7)) % 7;
    for (let i = 0; i < remaining; i++) {
      days.push({ date: null, dayNum: 0, isToday: false, isSelected: false, isOtherMonth: true, isDisabled: true });
    }

    return days;
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
}
