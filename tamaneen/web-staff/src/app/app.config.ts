import { ApplicationConfig, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { IdleService } from './core/auth/idle.service';
import { acceptLanguageInterceptor } from './core/i18n/accept-language.interceptor';
import { DEFAULT_LANGUAGE } from './core/i18n/language';
import { LanguageService } from './core/i18n/language.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideAnimationsAsync(),
    provideHttpClient(withInterceptors([acceptLanguageInterceptor, authInterceptor])),
    provideTranslateService({
      fallbackLang: DEFAULT_LANGUAGE,
      // Translation files are static assets: load them through HttpBackend so API interceptors don't apply.
      loader: provideTranslateHttpLoader({ prefix: './assets/i18n/', suffix: '.json', useHttpBackend: true }),
    }),
    // Runs before the first render so the portal never flashes in the wrong language or direction,
    // and the guards already know whether the refresh cookie still holds a session.
    provideAppInitializer(async () => {
      const auth = inject(AuthService);
      await inject(LanguageService).init();
      await auth.restore();
      // S32: 15-minute idle sign-out for every staff session.
      inject(IdleService);
    }),
  ],
};
