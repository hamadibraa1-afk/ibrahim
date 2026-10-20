import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { ConsentsForm } from '@/components/onboarding/ConsentsForm';
import { PageHeading } from '@/components/ui/PageHeading';
import { StepIndicator } from '@/components/ui/StepIndicator';
import { serverGet } from '@/lib/api/server';
import type { Consent } from '@/lib/api/types';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'consents' });
  return { title: t('metaTitle') };
}

/** P03 Consents (signed in), step 3 of 4. */
export default async function ConsentsPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, consents] = await Promise.all([getTranslations('consents'), serverGet<Consent[]>('me/consents')]);
  return (
    <div className="flex flex-col gap-4 pt-2">
      <StepIndicator step={3} total={4} />
      <PageHeading title={t('title')} subtitle={t('subtitle')} />
      <ConsentsForm consents={consents} />
    </div>
  );
}
