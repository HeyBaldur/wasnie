import { REMOVE_STYLES_ON_COMPONENT_DESTROY } from '@angular/platform-browser';
import {
  ApplicationConfig,
  ErrorHandler,
  inject,
  provideBrowserGlobalErrorListeners,
  provideAppInitializer,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideRouter, TitleStrategy } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { firstValueFrom } from 'rxjs';
import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { correlationIdInterceptor } from './core/interceptors/correlation-id.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { forbiddenResponseInterceptor } from './core/interceptors/forbidden-response.interceptor';
import { paymentRequiredInterceptor } from './core/interceptors/payment-required.interceptor';
import { ConsoleErrorTrackingService } from './core/observability/console-error-tracking.service';
import { ErrorTrackingService } from './core/observability/error-tracking.service';
import { GlobalErrorHandler } from './core/observability/global-error-handler';
import { ThemeService } from './core/theme/theme.service';
import { AuthService } from './core/services/auth.service';
import { CurrentUserService } from './core/auth/current-user.service';
import { TranslatedTitleStrategy } from './core/title-strategy';

const SUPPORTED_LANGS = ['en', 'es', 'pl'];

export const appConfig: ApplicationConfig = {
  providers: [
    /**
     * ★★ KEEP A DESTROYED COMPONENT'S STYLES IN THE DOCUMENT. Angular 17+ removes a component's
     * `<style>` when its LAST instance is destroyed, and `ws-modal` animates an inert CLONE of the
     * closed dialog out over 220ms — so a modal whose only `ws-select` had just been destroyed left a
     * ghost carrying the right `_ngcontent` attribute and nothing to match it. Measured: the trigger
     * went `display: flex → block`, `height: 32px → 35px`, `border: 1px → 0`, background transparent.
     * On screen that is the reported "for a millisecond the dropdown loses everything and you see
     * only letters".
     *
     * ★ FIXED HERE RATHER THAN IN THE MODAL, because the modal is not the only thing that outlives a
     * component's node: any exit animation on removed content has the same hole, and inlining computed
     * styles onto the clone would be a second rendering of every rule, lossy for pseudo-elements and
     * media queries.
     *
     * ★ THE COST IS A STYLE ELEMENT THAT STAYS. It is what Angular ≤16 did by default, the sheets are
     * deduplicated per component, and this application loads every one of them on the first visit to
     * the screen that uses it anyway.
     */
    { provide: REMOVE_STYLES_ON_COMPONENT_DESTROY, useValue: false },
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    { provide: TitleStrategy, useClass: TranslatedTitleStrategy },
    provideHttpClient(withInterceptors([correlationIdInterceptor, authInterceptor, errorInterceptor, forbiddenResponseInterceptor, paymentRequiredInterceptor])),
    { provide: ErrorHandler, useClass: GlobalErrorHandler },
    { provide: ErrorTrackingService, useClass: ConsoleErrorTrackingService },
    provideTranslateService({ defaultLanguage: 'en' }),
    provideTranslateHttpLoader({ prefix: './assets/i18n/', suffix: '.json' }),
    provideAppInitializer(() => {
      inject(ThemeService).init();
      const translate = inject(TranslateService);
      translate.setDefaultLang('en');
      const browserLang = translate.getBrowserLang() ?? 'en';
      const lang = SUPPORTED_LANGS.includes(browserLang) ? browserLang : 'en';
      return firstValueFrom(translate.use(lang));
    }),
    provideAppInitializer(() => {
      const authService = inject(AuthService);
      const currentUser = inject(CurrentUserService);
      if (authService.isAuthenticated()) {
        return firstValueFrom(currentUser.refresh());
      }
      return Promise.resolve();
    }),
  ],
};
