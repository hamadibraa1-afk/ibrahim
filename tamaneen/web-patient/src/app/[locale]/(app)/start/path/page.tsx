import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { ChoosePath } from '@/components/onboarding/ChoosePath';
import { PageHeading } from '@/components/ui/PageHeading';
import { getMe } from '@/lib/api/server';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'choosePath' });
  return { title: t('metaTitle') };
}

/** P22 Choose path (signed in). */
export default async function ChoosePathPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, me] = await Promise.all([getTranslations('choosePath'), getMe()]);
  return (
    <div className="flex flex-col gap-5 pt-2">
      <PageHeading title={t('title')} />
      <ChoosePath current={me.onboarding.path} />
    </div>
  );
}
