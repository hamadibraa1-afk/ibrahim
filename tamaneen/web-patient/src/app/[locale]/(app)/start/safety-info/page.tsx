import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { SafetyInfoForm } from '@/components/onboarding/SafetyInfoForm';
import { PageHeading } from '@/components/ui/PageHeading';
import { StepIndicator } from '@/components/ui/StepIndicator';
import { serverGetOptional } from '@/lib/api/server';
import type { SafetyInfo } from '@/lib/api/types';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'safetyInfo' });
  return { title: t('metaTitle') };
}

/** P21 Safety info (signed in), step 4 of 4, optional. */
export default async function SafetyInfoPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, initial] = await Promise.all([getTranslations('safetyInfo'), serverGetOptional<SafetyInfo>('me/safety-info')]);
  return (
    <div className="flex flex-col gap-4 pt-2">
      <StepIndicator step={4} total={4} optional />
      <PageHeading title={t('title')} subtitle={t('subtitle')} />
      <SafetyInfoForm initial={initial} />
    </div>
  );
}
