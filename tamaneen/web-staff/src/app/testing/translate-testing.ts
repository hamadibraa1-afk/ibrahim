import { TranslateLoader, provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import ar from '../../assets/i18n/ar.json';
import en from '../../assets/i18n/en.json';

/** Real translation files, loaded synchronously for tests. */
export const testTranslations = { ar, en } as const;

export function provideTestTranslations() {
  return provideTranslateService({
    fallbackLang: 'ar',
    loader: { provide: TranslateLoader, useValue: { getTranslation: (lang: 'ar' | 'en') => of(testTranslations[lang]) } },
  });
}
