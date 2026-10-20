import { unstable_rethrow } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { CallLink } from '@/components/crisis/CallLink';
import { CrisisStatusPanel } from '@/components/crisis/CrisisStatusPanel';
import { Link } from '@/i18n/navigation';
import { serverGet } from '@/lib/api/server';
import type { CrisisStatus } from '@/lib/api/types';
import { loadSafetyNumbers } from '@/lib/platform-config';

/**
 * P06 Crisis. Opened when the crisis gate blocks, or from the help button. The emergency numbers are
 * rendered here on the server as tel: links, so they work even if JavaScript or the API fails. No bottom
 * navigation and nothing else to tap besides help.
 */
export default async function CrisisPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations('crisis');

  let status: CrisisStatus | null = null;
  const numbersPromise = loadSafetyNumbers();
  try {
    status = await serverGet<CrisisStatus>('crisis/status');
  } catch (e) {
    unstable_rethrow(e);
    status = null;
  }
  const { emergencyNumber, supportLine } = await numbersPromise;
  const active = status !== null && (status.eventOpen || status.locked);

  return (
    <div className="flex flex-col gap-6 pt-2 text-ink">
      <div aria-hidden="true" className="h-2 rounded-full bg-help" />

      <div className="flex flex-col gap-3">
        <h1 className="text-3xl font-bold leading-snug">{t('title')}</h1>
        {active ? <p className="text-lg leading-relaxed">{t('subtitle')}</p> : null}
      </div>

      <CrisisStatusPanel initial={status} />

      <section aria-labelledby="crisis-danger" className="flex flex-col gap-3">
        <h2 id="crisis-danger" className="text-lg font-bold">
          {t('dangerTitle')}
        </h2>
        {emergencyNumber ? (
          <CallLink number={emergencyNumber} icon>
            {t('callEmergency')}
          </CallLink>
        ) : (
          <div className="flex flex-col gap-2 rounded-[18px] bg-help px-5 py-4 text-surface">
            <p className="text-lg font-bold">{t('noNumber')}</p>
            <Link href="/crisis-resources" className="font-semibold underline underline-offset-4">
              {t('resourcesLink')}
            </Link>
          </div>
        )}
        {supportLine ? (
          <CallLink number={supportLine} variant="outline">
            {t('supportLine')}
          </CallLink>
        ) : null}
      </section>

      <section aria-labelledby="crisis-until" className="flex flex-col gap-2 rounded-card bg-tint-sand p-5">
        <h2 id="crisis-until" className="text-lg font-bold">
          {t('untilTitle')}
        </h2>
        <p className="leading-relaxed">{t('untilBody')}</p>
      </section>

      <p className="text-center text-sm leading-relaxed text-muted">{t('chatPaused')}</p>

      <Link href="/settings/safety" className="flex min-h-touch items-center justify-center text-center font-semibold text-primary underline underline-offset-4">
        {t('updateNumber')}
      </Link>
    </div>
  );
}
