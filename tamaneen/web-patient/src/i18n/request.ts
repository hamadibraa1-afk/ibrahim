import { getRequestConfig } from 'next-intl/server';
import { namespaces } from './namespaces';
import { isLocale, routing, type Locale } from './routing';

export async function loadMessages(locale: Locale): Promise<Record<string, unknown>> {
  const parts = await Promise.all(
    namespaces.map(async (ns) => {
      try {
        return (await import(`../../messages/${locale}/${ns}.json`)).default as Record<string, unknown>;
      } catch {
        return {};
      }
    }),
  );
  return Object.assign({}, ...parts);
}

export default getRequestConfig(async ({ requestLocale }) => {
  const requested = await requestLocale;
  const locale = isLocale(requested) ? requested : routing.defaultLocale;
  return {
    locale,
    messages: await loadMessages(locale),
    timeZone: 'Asia/Riyadh',
  };
});
