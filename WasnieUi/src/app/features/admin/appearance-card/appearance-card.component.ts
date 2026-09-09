import { Component, inject, signal } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ThemeMode, ThemeService } from '../../../core/theme/theme.service';
import { LanguageFlagComponent } from '../../../shared/components/language-flag/language-flag.component';

const SUPPORTED_LANGS = ['en', 'es', 'pl'] as const;
type SupportedLang = (typeof SUPPORTED_LANGS)[number];

/**
 * ★ LA CLAVE ESTÁ ESCRITA, NO ARMADA (§C2). `'LANGUAGE.' + code` habría salido sola de esta lista, y
 * es exactamente lo que la regla prohíbe: un código que el build no conoce imprimiría en pantalla un
 * identificador interno en vez de un idioma.
 *
 * ★ Y SON ENDÓNIMOS. Igual que en el selector de las pantallas sin sesión: quien anda buscando
 * "Polski" no reconoce "Polish", así que las tres claves resuelven a la misma cadena en en/es/pl.
 */
const LANGUAGE_OPTIONS: { code: SupportedLang; labelKey: string }[] = [
  { code: 'en', labelKey: 'LANGUAGE.EN' },
  { code: 'es', labelKey: 'LANGUAGE.ES' },
  { code: 'pl', labelKey: 'LANGUAGE.PL' },
];

/** Must match the key read by App.ngOnInit so the choice survives a reload. */
const LANG_STORAGE_KEY = 'wasnie_lang';

const THEME_OPTIONS: { mode: ThemeMode; labelKey: string; icon: string }[] = [
  { mode: 'light', labelKey: 'THEME.LIGHT', icon: '☀️' },
  { mode: 'soft', labelKey: 'THEME.SOFT', icon: '🌤' },
  { mode: 'dark', labelKey: 'THEME.DARK', icon: '🌙' },
  { mode: 'system', labelKey: 'THEME.SYSTEM', icon: '💻' },
];

@Component({
  selector: 'app-appearance-card',
  standalone: true,
  imports: [TranslatePipe, LanguageFlagComponent],
  templateUrl: './appearance-card.component.html',
  styleUrl: './appearance-card.component.scss',
})
export class AppearanceCardComponent {
  private readonly translate = inject(TranslateService);
  private readonly themeService = inject(ThemeService);

  readonly languageOptions = LANGUAGE_OPTIONS;
  readonly themeOptions = THEME_OPTIONS;
  readonly currentLang = signal<SupportedLang>(
    (this.translate.currentLang as SupportedLang) ?? 'en'
  );
  readonly currentTheme = this.themeService.mode;

  switchLanguage(lang: SupportedLang): void {
    this.translate.use(lang);
    localStorage.setItem(LANG_STORAGE_KEY, lang);
    this.currentLang.set(lang);
  }

  selectTheme(mode: ThemeMode): void {
    this.themeService.setMode(mode);
  }
}
