import { TestBed } from '@angular/core/testing';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { CurrencyFormatPipe } from './currency-format.pipe';

describe('CurrencyFormatPipe', () => {
  let pipe: CurrencyFormatPipe;
  let translate: TranslateService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TranslateModule.forRoot()],
    });
    translate = TestBed.inject(TranslateService);
    translate.setDefaultLang('en');
    translate.use('en');
    pipe = TestBed.runInInjectionContext(() => new CurrencyFormatPipe());
  });

  it('formats zero EUR as € symbol, not $', () => {
    const result = pipe.transform(0, 'EUR');
    expect(result).toContain('0');
    expect(result).not.toContain('$');
    // Intl formats EUR with € in en locale
    expect(result).toMatch(/€|EUR/);
  });

  it('formats zero USD with $ symbol', () => {
    const result = pipe.transform(0, 'USD');
    expect(result).toContain('$');
    expect(result).not.toContain('€');
  });

  it('formats positive EUR amount', () => {
    const result = pipe.transform(1234.56, 'EUR');
    expect(result).toContain('1,234');
    expect(result).toMatch(/€|EUR/);
  });

  it('formats positive PLN amount', () => {
    const result = pipe.transform(500, 'PLN');
    expect(result).toContain('500');
    expect(result).toMatch(/PLN|zł/);
  });

  it('returns empty string for null value', () => {
    expect(pipe.transform(null, 'EUR')).toBe('');
  });

  it('returns empty string for undefined value', () => {
    expect(pipe.transform(undefined, 'EUR')).toBe('');
  });

  it('returns visible error marker for null currency — surfaces corrupt data', () => {
    const result = pipe.transform(0, null);
    expect(result).toContain('???');
  });

  it('returns visible error marker for empty string currency', () => {
    const result = pipe.transform(100, '');
    expect(result).toContain('???');
  });

  it('returns visible error marker for whitespace-only currency', () => {
    const result = pipe.transform(50, '   ');
    expect(result).toContain('???');
  });

  it('preserves trailing zero — EUR 15934.6 shows two decimal places', () => {
    const result = pipe.transform(15934.6, 'EUR');
    // Must end in ".60", not ".6" — minimumFractionDigits must not be overridden to 0
    expect(result).toMatch(/15[,.]?934[,.]?60/);
    expect(result).not.toMatch(/15[,.]?934[,.]?6[^0]/);
  });

  it('always shows two decimal places for EUR whole amounts', () => {
    const result = pipe.transform(1000, 'EUR');
    expect(result).toMatch(/1[,.]?000[,.]?00/);
  });

  it('shows zero decimal places for JPY — currency native fraction digits', () => {
    const result = pipe.transform(15935, 'JPY');
    // JPY has 0 standard fraction digits — must not show ".00" or ".0"
    expect(result).not.toContain('.');
    expect(result).toContain('15');
  });
});
