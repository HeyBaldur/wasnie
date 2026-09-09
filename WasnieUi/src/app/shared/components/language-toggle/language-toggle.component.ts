import {
  Component,
  HostListener,
  inject,
  signal,
} from '@angular/core';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { LanguageFlagComponent } from '../language-flag/language-flag.component';

interface LanguageOption {
  /** The ngx-translate language code. */
  code: string;
  /** The key for this language's name — see the note on why it is NOT translated. */
  labelKey: string;
  /** The flag, drawn inline — see the template on why this is not an emoji. */
}

/**
 * ★★ THE LABELS ARE ENDONYMS, NOT TRANSLATIONS, AND THAT IS THE WHOLE POINT OF A LANGUAGE PICKER.
 *    Every other label in this app is translated into the CURRENT language; this one must not be. A
 *    Polish speaker who has landed on the English build is looking for the word "Polski" — translating
 *    the option into English gives them "Polish", which is the one word they may not recognise. So the
 *    keys below resolve to the same string in all three files.
 *
 * ★ THE FLAG IS A COUNTRY AND THE LANGUAGE IS NOT — a known imprecision, taken deliberately because the
 *   ticket asks for flags and because a flag is what a person scans for. It stays paired with the
 *   language's own name so the control never depends on the flag alone to be understood.
 */
const OPTIONS: LanguageOption[] = [
  { code: 'en', labelKey: 'LANGUAGE.EN' },
  { code: 'es', labelKey: 'LANGUAGE.ES' },
  { code: 'pl', labelKey: 'LANGUAGE.PL' },
];

/** Must match the key read by App.ngOnInit, or the choice does not survive a reload. */
const LANG_STORAGE_KEY = 'wasnie_lang';

/**
 * The language picker for the signed-OUT pages.
 *
 * ★★ IT EXISTS BECAUSE THE ONE PLACE THE LANGUAGE COULD BE CHANGED WAS BEHIND THE LOGIN. Appearance
 * settings live in admin, so a Polish user facing an English sign-in form had no way to read it — the
 * control they needed was on the other side of the door they could not open.
 *
 * ★ MIRRORS ThemeToggleComponent DELIBERATELY (§5.1): same trigger, same popover, same roving-focus
 * keyboard handling, same close-on-outside-click. The two sit side by side on the auth pages, and two
 * dropdowns a centimetre apart that behave differently is a defect the user feels before they can name.
 */
@Component({
  selector: 'app-language-toggle',
  standalone: true,
  imports: [TranslateModule, LanguageFlagComponent],
  templateUrl: './language-toggle.component.html',
  styleUrl: './language-toggle.component.scss',
})
export class LanguageToggleComponent {
  private readonly translate = inject(TranslateService);

  readonly options = OPTIONS;
  readonly isOpen = signal(false);
  readonly focusedIndex = signal(0);

  /**
   * ★ SEEDED FROM WHAT IS ACTUALLY IN FORCE, not from storage. On the very first visit nothing is
   *   stored and the app falls back to English; reading storage here would leave the control showing
   *   no flag at all on exactly the screen where it matters most.
   */
  readonly currentLang = signal<string>(
    this.translate.currentLang || this.translate.getDefaultLang() || 'en'
  );

  toggle(): void {
    const opening = !this.isOpen();
    this.isOpen.set(opening);
    if (opening) {
      this.focusedIndex.set(
        Math.max(0, OPTIONS.findIndex((o) => o.code === this.currentLang()))
      );
    }
  }

  select(code: string): void {
    this.translate.use(code);
    try {
      localStorage.setItem(LANG_STORAGE_KEY, code);
    } catch {
      // A browser refusing storage costs the choice its persistence, never the switch itself.
    }
    this.currentLang.set(code);
    this.isOpen.set(false);
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (!this.isOpen()) return;

    switch (event.key) {
      case 'Escape':
        this.isOpen.set(false);
        break;
      case 'ArrowDown':
        event.preventDefault();
        this.focusedIndex.update((i) => (i + 1) % OPTIONS.length);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.focusedIndex.update((i) => (i - 1 + OPTIONS.length) % OPTIONS.length);
        break;
      case 'Enter':
      case ' ':
        event.preventDefault();
        this.select(OPTIONS[this.focusedIndex()].code);
        break;
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (!target.closest('app-language-toggle')) {
      this.isOpen.set(false);
    }
  }
}
