import { Component, inject, signal } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ThemeMode, ThemeService } from '../../../core/theme/theme.service';

const SUPPORTED_LANGS = ['en', 'es', 'pl'] as const;
type SupportedLang = (typeof SUPPORTED_LANGS)[number];

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
  imports: [TranslatePipe],
  templateUrl: './appearance-card.component.html',
  styleUrl: './appearance-card.component.scss',
})
export class AppearanceCardComponent {
  private readonly translate = inject(TranslateService);
  private readonly themeService = inject(ThemeService);

  readonly supportedLangs = SUPPORTED_LANGS;
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
