'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useSearchParams } from 'next/navigation';
import { useTransition } from 'react';
import { usePathname, useRouter } from '@/i18n/navigation';
import { isLocale, otherLocale, routing } from '@/i18n/routing';

/**
 * Switches the whole app between Arabic and English. The URL prefix changes (/ar ↔ /en), the
 * middleware stores the choice in NEXT_LOCALE, and the layout re-renders <html lang dir> so the page
 * flips between RTL and LTR. The label is written in the target language so people who can't read the
 * current one can still find it.
 */
export function LanguageToggle() {
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  const target = otherLocale(locale);
  const t = useTranslations('languageToggle');
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const [pending, startTransition] = useTransition();

  function switchLanguage() {
    const query = searchParams.toString();
    startTransition(() => {
      router.replace(query ? `${pathname}?${query}` : pathname, { locale: target });
    });
  }

  return (
    <button
      type="button"
      onClick={switchLanguage}
      disabled={pending}
      aria-label={t('label')}
      data-testid="language-toggle"
      className="inline-flex min-h-touch items-center gap-2 rounded-full border border-line bg-surface px-4 text-sm font-semibold text-primary hover:bg-tint-teal disabled:opacity-60"
    >
      <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <circle cx="12" cy="12" r="9" />
        <path d="M3 12h18M12 3c2.5 2.7 2.5 15.3 0 18M12 3c-2.5 2.7-2.5 15.3 0 18" />
      </svg>
      <span lang={target}>{t('target')}</span>
    </button>
  );
}
