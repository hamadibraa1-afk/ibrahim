import { ApplicationConfig, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { environment } from '../environments/environment';
import { routes } from './app.routes';
import { acceptLanguageInterceptor } from './core/i18n/accept-language.interceptor';
import { DEFAULT_LANGUAGE } from './core/i18n/language';
import { LanguageService } from './core/i18n/language.service';
import { SessionService } from './core/session/session.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideAnimationsAsync(),
    provideHttpClient(withInterceptors([acceptLanguageInterceptor])),
    provideTranslateService({
      fallbackLang: DEFAULT_LANGUAGE,
      // Translation files are static assets: load them through HttpBackend so API interceptors don't apply.
      loader: provideTranslateHttpLoader({ prefix: './assets/i18n/', suffix: '.json', useHttpBackend: true }),
    }),
    // Runs before the first render so the portal never flashes in the wrong language or direction.
    provideAppInitializer(() => {
      inject(SessionService).setRoles(environment.devRoles);
      return inject(LanguageService).init();
    }),
  ],
};
