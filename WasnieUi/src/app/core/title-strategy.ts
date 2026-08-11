import { inject, Injectable } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';

/** Brand suffix shown on every tab, and the standalone title when a route has none. */
const BRAND = 'Incentra | ICM & SPM';

/**
 * Resolves a route's `title` as an i18n key, translates it, and renders the
 * document title as `<Page> | Incentra`. Falls back to the brand when a route has
 * no title or the key is missing, and re-applies on language change.
 */
@Injectable({ providedIn: 'root' })
export class TranslatedTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly translate = inject(TranslateService);
  private currentKey: string | null = null;

  constructor() {
    super();
    this.translate.onLangChange.subscribe(() => this.apply());
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.currentKey = this.buildTitle(snapshot) ?? null;
    this.apply();
  }

  private apply(): void {
    if (!this.currentKey) {
      this.title.setTitle(BRAND);
      return;
    }
    const translated = this.translate.instant(this.currentKey);
    const page = translated && translated !== this.currentKey ? translated : null;
    this.title.setTitle(page ? `${page} | Incentra` : BRAND);
  }
}
