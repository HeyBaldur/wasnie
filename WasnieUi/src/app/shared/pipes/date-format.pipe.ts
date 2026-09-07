import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

@Pipe({ name: 'dateFormat', standalone: true, pure: false })
export class DateFormatPipe implements PipeTransform {
  private readonly translate = inject(TranslateService);

  /**
   * @param timeStyle when given, the clock time is appended — omitted by default (KAN-19).
   *
   * ★★ ADDITIVE, AND DELIBERATELY OPTIONAL. Every existing caller passes one argument or none and
   * keeps rendering exactly what it rendered before; only a caller that asks for a time gets one.
   * Making the time unconditional would have put a clock on every date column in the app — a period
   * end, a close date, a quota window — where it is noise.
   *
   * ★ THE AUDIT TRAIL NEEDS IT AND A DATE ALONE WOULD BE WRONG THERE. The log's whole claim is "who
   * did what WHEN"; with a date only, the fourteen rows a bulk import writes in the same second are
   * indistinguishable, and the order the table sorts by becomes invisible to the reader.
   */
  transform(
    value: string | null | undefined,
    dateStyle: Intl.DateTimeFormatOptions['dateStyle'] = 'medium',
    timeStyle?: Intl.DateTimeFormatOptions['timeStyle']
  ): string {
    if (!value) return '';
    const locale = this.translate.currentLang ?? 'en';
    const date = new Date(value.includes('T') ? value : value + 'T00:00:00');
    return new Intl.DateTimeFormat(locale, timeStyle ? { dateStyle, timeStyle } : { dateStyle })
      .format(date);
  }
}
