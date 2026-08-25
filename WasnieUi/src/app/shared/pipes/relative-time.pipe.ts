import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

/**
 * Impure by design: the output depends on "now", so it must be re-evaluated on every change
 * detection pass. That makes it time-dependent, and dev-mode runs a second verification pass right
 * after the first — if the clock crossed a second between the two, the string changes ('15 seconds
 * ago' -> '16 seconds ago') and Angular throws NG0100. The result is therefore memoized per
 * (value, locale) for a short window, so every pass of the same cycle sees an identical string
 * while the label still refreshes as time goes by.
 */
const REFRESH_WINDOW_MS = 1_000;

@Pipe({ name: 'relativeTime', standalone: true, pure: false })
export class RelativeTimePipe implements PipeTransform {
  private readonly translate = inject(TranslateService);

  private cacheKey: string | null = null;
  private cacheText = '';
  private cacheAt = 0;

  transform(value: string | null | undefined): string {
    if (!value) return '';
    const locale = this.translate.currentLang ?? 'en';
    const key = `${value}|${locale}`;
    const now = Date.now();

    if (key === this.cacheKey && now - this.cacheAt < REFRESH_WINDOW_MS) {
      return this.cacheText;
    }

    const diffMs = new Date(value).getTime() - now;
    const rtf = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });

    const abs = Math.abs(diffMs);
    let text: string;
    if (abs < 60_000) text = rtf.format(Math.round(diffMs / 1000), 'second');
    else if (abs < 3_600_000) text = rtf.format(Math.round(diffMs / 60_000), 'minute');
    else if (abs < 86_400_000) text = rtf.format(Math.round(diffMs / 3_600_000), 'hour');
    else if (abs < 2_592_000_000) text = rtf.format(Math.round(diffMs / 86_400_000), 'day');
    else text = rtf.format(Math.round(diffMs / 2_592_000_000), 'month');

    this.cacheKey = key;
    this.cacheText = text;
    this.cacheAt = now;
    return text;
  }
}
