import { getTranslations, setRequestLocale } from 'next-intl/server';
import { PhoneNumber } from '@/components/crisis/PhoneNumber';
import { RetryButton } from '@/components/pwa/RetryButton';
import { StatePanel } from '@/components/pwa/StatePanel';
import { Link } from '@/i18n/navigation';
import { loadSafetyNumbers } from '@/lib/platform-config';

// Rendered per request and precached by the service worker, which serves it when a page can't load offline.
export const dynamic = 'force-dynamic';

/** P35 Offline. */
export default async function OfflinePage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, { emergencyNumber, supportLine }] = await Promise.all([getTranslations('systemStates.offline'), loadSafetyNumbers()]);
  const lines = [emergencyNumber, supportLine].filter((n): n is string => Boolean(n));

  return (
    <StatePanel icon="offline" title={t('title')} body={t('body')}>
      <div className="flex flex-col items-center gap-1 rounded-[16px] bg-help/10 px-4 py-3 text-sm text-help">
        {lines.length > 0 ? (
          <>
            <span>{t('numbers')}</span>
            <span className="flex flex-wrap items-center justify-center gap-x-3 font-bold">
              {lines.map((n) => (
                <a key={n} href={`tel:${n}`} className="inline-flex min-h-touch items-center underline underline-offset-4">
                  <PhoneNumber number={n} />
                </a>
              ))}
            </span>
          </>
        ) : (
          <Link href="/crisis-resources" className="inline-flex min-h-touch items-center font-bold underline underline-offset-4">
            {t('noNumbers')}
          </Link>
        )}
      </div>
      <RetryButton label={t('retry')} />
    </StatePanel>
  );
}
