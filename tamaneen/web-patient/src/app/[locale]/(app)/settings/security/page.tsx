import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { SecurityPanel } from '@/components/settings/SecurityPanel';
import { BackLink } from '@/components/ui/BackLink';
import { PageHeading } from '@/components/ui/PageHeading';
import { serverGet } from '@/lib/api/server';
import type { SecurityOverview } from '@/lib/api/types';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'settings.security' });
  return { title: t('title') };
}

/** P32 Security. */
export default async function SecurityPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [overview, t, ts] = await Promise.all([serverGet<SecurityOverview>('me/security'), getTranslations('settings.security'), getTranslations('settings')]);
  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href="/settings" label={ts('back')} />
      <PageHeading title={t('title')} />
      <SecurityPanel overview={overview} nowIso={new Date().toISOString()} />
    </div>
  );
}
