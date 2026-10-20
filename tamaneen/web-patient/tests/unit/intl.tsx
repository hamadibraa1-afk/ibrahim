import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { render, type RenderResult } from '@testing-library/react';
import { NextIntlClientProvider } from 'next-intl';
import type { ReactElement } from 'react';

type Messages = Record<string, unknown>;
const cache: Partial<Record<'ar' | 'en', Messages>> = {};

/** All message namespaces of one language, merged like src/i18n/request.ts does. */
export function messagesFor(locale: 'ar' | 'en'): Messages {
  if (!cache[locale]) {
    const dir = join(__dirname, '../../messages', locale);
    cache[locale] = Object.assign({}, ...readdirSync(dir).map((f) => JSON.parse(readFileSync(join(dir, f), 'utf8')) as Messages));
  }
  return cache[locale]!;
}

/** Renders a client component inside next-intl with the real translations, in Arabic or English. */
export function renderWithIntl(ui: ReactElement, locale: 'ar' | 'en' = 'ar'): RenderResult {
  document.documentElement.lang = locale;
  document.documentElement.dir = locale === 'ar' ? 'rtl' : 'ltr';
  return render(
    <NextIntlClientProvider locale={locale} messages={messagesFor(locale)} timeZone="Asia/Riyadh">
      {ui}
    </NextIntlClientProvider>,
  );
}
