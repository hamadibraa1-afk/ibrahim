import type { Metadata } from 'next';
import { unstable_rethrow } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { AfterCrisisView } from '@/components/afterCrisis/AfterCrisisView';
import { CallLink } from '@/components/crisis/CallLink';
import { RetryButton } from '@/components/pwa/RetryButton';
import { Link, redirect } from '@/i18n/navigation';
import { isLocale, routing } from '@/i18n/routing';
import { getMe, serverGet } from '@/lib/api/server';
import type { CrisisFollowUp } from '@/lib/api/types';
import { DEFAULT_TIME_ZONE } from '@/lib/format';
import { loadSafetyNumbers } from '@/lib/platform-config';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'afterCrisis' });
  return { title: t('metaTitle') };
}

/**
 * P38 (route /crisis/after). Only after a clinician closed the event (EC-28): while one is open the patient sees
 * «نتواصل معك الآن» with the emergency numbers; with nothing to follow up, home. The numbers are server-rendered
 * tel: links, so they work even when the follow-up cannot load.
 */
export default async function AfterCrisisPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations('afterCrisis');
  const tc = await getTranslations('crisis');
  const lang = isLocale(locale) ? locale : routing.defaultLocale;
  const [numbers, me] = await Promise.all([loadSafetyNumbers(), getMe()]);
  let followUp: CrisisFollowUp | null = null;
  try {
    followUp = await serverGet<CrisisFollowUp>('crisis/follow-up');
  } catch (e) {
    unstable_rethrow(e);
  }

  const resources = (
    <div className="flex flex-col gap-3">
      {numbers.emergencyNumber ? <CallLink number={numbers.emergencyNumber} icon>{tc('callEmergency')}</CallLink> : null}
      {numbers.supportLine ? <CallLink number={numbers.supportLine} variant="outline">{tc('supportLine')}</CallLink> : null}
      <Link href="/crisis-resources" className="text-center font-semibold text-primary underline underline-offset-4">{tc('resourcesLink')}</Link>
    </div>
  );

  if (followUp === null) {
    return (
      <div role="alert" className="flex flex-col gap-4 pt-6">
        <h1 className="text-xl font-bold">{t('errorTitle')}</h1>
        <p className="text-muted">{t('errorBody')}</p>
        <RetryButton label={t('retry')} />
        {resources}
      </div>
    );
  }

  if (followUp.state === 'none') redirect({ href: '/home', locale: lang });

  if (followUp.state === 'open') {
    return (
      <div className="flex flex-col gap-5 pt-4">
        <h1 className="text-2xl font-bold">{t('contactingTitle')}</h1>
        <p className="leading-relaxed">{t('contactingBody')}</p>
        {resources}
      </div>
    );
  }

  return <AfterCrisisView followUp={followUp} timeZone={me.timeZone || DEFAULT_TIME_ZONE} resources={resources} />;
}
