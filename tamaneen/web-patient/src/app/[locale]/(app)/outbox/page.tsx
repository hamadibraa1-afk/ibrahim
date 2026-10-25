import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { CallLink } from '@/components/crisis/CallLink';
import { OutboxView } from '@/components/pwa/OutboxView';
import { BackLink } from '@/components/ui/BackLink';
import { PageHeading } from '@/components/ui/PageHeading';
import { Link } from '@/i18n/navigation';
import { loadSafetyNumbers } from '@/lib/platform-config';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'systemStates.outbox' });
  return { title: t('metaTitle') };
}

/**
 * P66 Weak connection: what is waiting on this phone and what will be sent, and the emergency call, which
 * needs no internet (a server-rendered tel: link).
 */
export default async function OutboxPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, { emergencyNumber }] = await Promise.all([getTranslations('systemStates.outbox'), loadSafetyNumbers()]);
  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href="/home" label={t('home')} />
      <PageHeading title={t('title')} />
      <OutboxView />
      <section aria-label={t('call')} className="flex flex-col gap-3 rounded-card border-[1.5px] border-help/30 bg-surface p-4">
        <p className="leading-relaxed">{t('emergency')}</p>
        {emergencyNumber ? (
          <CallLink number={emergencyNumber} variant="outline">
            {t('call')}
          </CallLink>
        ) : (
          <Link href="/crisis-resources" className="inline-flex min-h-touch items-center font-bold text-help underline">
            {t('resources')}
          </Link>
        )}
      </section>
    </div>
  );
}
