'use client';

import { useTranslations } from 'next-intl';
import { Link } from '@/i18n/navigation';

/** Localized error boundary. The Help now button in the header stays usable. */
export default function ErrorPage({ reset }: { error: Error; reset: () => void }) {
  const t = useTranslations('errors');
  return (
    <div className="flex flex-col items-center gap-4 pt-16 text-center">
      <h1 className="text-2xl font-bold">{t('pageTitle')}</h1>
      <p className="max-w-md text-muted">{t('pageBody')}</p>
      <button type="button" onClick={reset} className="flex min-h-cta items-center rounded-full bg-primary px-6 font-semibold text-surface">
        {t('retry')}
      </button>
      <Link href="/crisis-resources" className="text-sm font-semibold text-help">
        {t('crisisHint')}
      </Link>
    </div>
  );
}
