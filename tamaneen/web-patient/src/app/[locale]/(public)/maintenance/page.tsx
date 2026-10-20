import { getTranslations, setRequestLocale } from 'next-intl/server';
import { PhoneNumber } from '@/components/crisis/PhoneNumber';
import { StatePanel } from '@/components/pwa/StatePanel';
import { Link } from '@/i18n/navigation';
import { isLocale, routing } from '@/i18n/routing';
import { formatTime } from '@/lib/format';
import { loadSafetyNumbers, publicPlatformConfig } from '@/lib/platform-config';

export const dynamic = 'force-dynamic';

/** P35 Scheduled maintenance. The return time comes from TAMANEEN_MAINTENANCE_UNTIL when the deployment sets it. */
export default async function MaintenancePage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale: raw } = await params;
  const locale = isLocale(raw) ? raw : routing.defaultLocale;
  setRequestLocale(locale);
  const [t, { emergencyNumber }] = await Promise.all([getTranslations('systemStates.maintenance'), loadSafetyNumbers()]);
  const { maintenanceUntil } = publicPlatformConfig();
  const back = maintenanceUntil && Date.parse(maintenanceUntil) > Date.now() ? formatTime(maintenanceUntil, locale) : null;

  return (
    <StatePanel icon="wrench" title={t('title')} body={back ? t('body', { time: back }) : t('bodySoon')}>
      <p className="flex flex-wrap items-center justify-center gap-x-2 rounded-[16px] bg-help/10 px-4 py-3 text-sm text-help">
        <span>{t('danger')}</span>
        {emergencyNumber ? (
          <a href={`tel:${emergencyNumber}`} className="inline-flex min-h-touch items-center font-bold underline underline-offset-4">
            <PhoneNumber number={emergencyNumber} />
          </a>
        ) : (
          <Link href="/crisis-resources" className="inline-flex min-h-touch items-center font-bold underline underline-offset-4">
            {t('resources')}
          </Link>
        )}
      </p>
    </StatePanel>
  );
}
