import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { SafetyForm } from '@/components/settings/SafetyForm';
import { BackLink } from '@/components/ui/BackLink';
import { PageHeading } from '@/components/ui/PageHeading';
import { getMe, serverGet } from '@/lib/api/server';
import type { SafetyInfo } from '@/lib/api/types';
import { DEFAULT_TIME_ZONE } from '@/lib/format';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'settings.safety' });
  return { title: t('title') };
}

/** P34 Emergency contact and location. */
export default async function SafetySettingsPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [info, me, t, ts] = await Promise.all([serverGet<SafetyInfo>('me/safety-info'), getMe(), getTranslations('settings.safety'), getTranslations('settings')]);
  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href="/settings" label={ts('back')} />
      <PageHeading title={t('title')} subtitle={t('intro')} />
      <SafetyForm initial={info} timeZone={me.timeZone || DEFAULT_TIME_ZONE} />
    </div>
  );
}
