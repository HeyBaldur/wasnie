import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

@Pipe({ name: 'relativeTime', standalone: true, pure: false })
export class RelativeTimePipe implements PipeTransform {
  private readonly translate = inject(TranslateService);

  transform(value: string | null | undefined): string {
    if (!value) return '';
    const locale = this.translate.currentLang ?? 'en';
    const date = new Date(value);
    const now = new Date();
    const diffMs = date.getTime() - now.getTime();
    const rtf = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });

    const abs = Math.abs(diffMs);
    if (abs < 60_000) return rtf.format(Math.round(diffMs / 1000), 'second');
    if (abs < 3_600_000) return rtf.format(Math.round(diffMs / 60_000), 'minute');
    if (abs < 86_400_000) return rtf.format(Math.round(diffMs / 3_600_000), 'hour');
    if (abs < 2_592_000_000) return rtf.format(Math.round(diffMs / 86_400_000), 'day');
    return rtf.format(Math.round(diffMs / 2_592_000_000), 'month');
  }
}
