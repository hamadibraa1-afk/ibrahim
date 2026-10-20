import { HttpBackend, HttpClient } from '@angular/common/http';
import { TranslateLoader } from '@ngx-translate/core';
import { TranslateHttpLoader } from '@ngx-translate/http-loader';

/**
 * Loads assets/i18n/{ar,en}.json. Uses HttpBackend directly so translation files bypass interceptors
 * (they are static assets, not API calls).
 */
export function createTranslateLoader(backend: HttpBackend): TranslateLoader {
  return new TranslateHttpLoader(new HttpClient(backend), './assets/i18n/', '.json');
}
