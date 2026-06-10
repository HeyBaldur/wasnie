import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

@Pipe({ name: 'currencyFormat', standalone: true, pure: false })
export class CurrencyFormatPipe implements PipeTransform {
  private readonly translate = inject(TranslateService);

  transform(value: number | null | undefined, currency?: string | null): string {
    if (value == null) return '';
    if (!currency || currency.trim() === '') {
      // A missing currency code means the data is corrupt — surface it visibly
      // so the bug is caught in review rather than silently defaulting to USD.
      return `??? ${value}`;
    }
    const locale = this.translate.currentLang ?? 'en';
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency,
    }).format(value);
  }
}
