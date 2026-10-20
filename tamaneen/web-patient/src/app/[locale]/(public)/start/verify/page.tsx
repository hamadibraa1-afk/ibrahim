import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { VerifyForm } from '@/components/onboarding/VerifyForm';
import { BackLink } from '@/components/ui/BackLink';
import { StepIndicator } from '@/components/ui/StepIndicator';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'verify' });
  return { title: t('metaTitle') };
}

/** P16 Verify (public), step 2 of 4. */
export default async function VerifyPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href="/start/signup" />
      <StepIndicator step={2} total={4} />
      <VerifyForm />
    </div>
  );
}
