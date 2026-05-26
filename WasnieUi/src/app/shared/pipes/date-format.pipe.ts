import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

@Pipe({ name: 'dateFormat', standalone: true, pure: false })
export class DateFormatPipe implements PipeTransform {
  private readonly translate = inject(TranslateService);

  transform(
    value: string | null | undefined,
    dateStyle: Intl.DateTimeFormatOptions['dateStyle'] = 'medium'
  ): string {
    if (!value) return '';
    const locale = this.translate.currentLang ?? 'en';
    const date = new Date(value.includes('T') ? value : value + 'T00:00:00');
    return new Intl.DateTimeFormat(locale, { dateStyle }).format(date);
  }
}
