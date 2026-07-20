import { TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';
import { signal } from '@angular/core';
import { AppearanceCardComponent } from './appearance-card.component';
import { ThemeService } from '../../../core/theme/theme.service';

describe('AppearanceCardComponent', () => {
  let translate: jasmine.SpyObj<TranslateService>;
  let theme: { mode: ReturnType<typeof signal>; setMode: jasmine.Spy };

  beforeEach(() => {
    translate = jasmine.createSpyObj<TranslateService>('TranslateService', ['use']);
    (translate as unknown as { currentLang: string }).currentLang = 'en';
    theme = { mode: signal('dark'), setMode: jasmine.createSpy('setMode') };

    TestBed.configureTestingModule({
      imports: [AppearanceCardComponent],
      providers: [
        { provide: TranslateService, useValue: translate },
        { provide: ThemeService, useValue: theme },
      ],
    });
    localStorage.removeItem('wasnie_lang');
  });

  afterEach(() => localStorage.removeItem('wasnie_lang'));

  function create(): AppearanceCardComponent {
    return TestBed.createComponent(AppearanceCardComponent).componentInstance;
  }

  it('applies the language and persists it so it survives a reload', () => {
    const cmp = create();

    cmp.switchLanguage('es');

    expect(translate.use).toHaveBeenCalledWith('es');
    expect(localStorage.getItem('wasnie_lang')).toBe('es');
    expect(cmp.currentLang()).toBe('es');
  });

  it('overwrites a previously persisted language', () => {
    localStorage.setItem('wasnie_lang', 'es');
    const cmp = create();

    cmp.switchLanguage('pl');

    expect(localStorage.getItem('wasnie_lang')).toBe('pl');
  });
});
