import { getTranslations } from 'next-intl/server';
import { Link } from '@/i18n/navigation';

/** Always visible. Leads to emergency resources (P14), which are cached by the service worker for offline use. */
export async function HelpNowLink() {
  const t = await getTranslations('common');
  return (
    <Link
      href="/crisis-resources"
      aria-label={t('helpNowLabel')}
      data-testid="help-now"
      className="inline-flex min-h-touch items-center rounded-full border-[1.5px] border-help bg-surface px-4 text-sm font-semibold text-help"
    >
      {t('helpNow')}
    </Link>
  );
}
