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
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    { provide: TitleStrategy, useClass: TranslatedTitleStrategy },
    provideHttpClient(withInterceptors([correlationIdInterceptor, authInterceptor, errorInterceptor, forbiddenResponseInterceptor])),
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
