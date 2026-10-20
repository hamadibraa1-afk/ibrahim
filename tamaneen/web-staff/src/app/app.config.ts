import { APP_INITIALIZER, ApplicationConfig, importProvidersFrom, inject } from '@angular/core';
import { HttpBackend, provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';
import { environment } from '../environments/environment';
import { routes } from './app.routes';
import { acceptLanguageInterceptor } from './core/i18n/accept-language.interceptor';
import { DEFAULT_LANGUAGE } from './core/i18n/language';
import { LanguageService } from './core/i18n/language.service';
import { createTranslateLoader } from './core/i18n/translate-loader';
import { SessionService } from './core/session/session.service';

function initializeApp(): () => Promise<void> {
  const language = inject(LanguageService);
  const session = inject(SessionService);
  return async () => {
    session.setRoles(environment.devRoles);
    await language.init();
  };
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes, withComponentInputBinding()),
    provideAnimationsAsync(),
    provideHttpClient(withInterceptors([acceptLanguageInterceptor])),
    importProvidersFrom(
      TranslateModule.forRoot({
        defaultLanguage: DEFAULT_LANGUAGE,
        loader: { provide: TranslateLoader, useFactory: createTranslateLoader, deps: [HttpBackend] },
      }),
    ),
    { provide: APP_INITIALIZER, useFactory: initializeApp, multi: true },
  ],
};
