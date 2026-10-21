import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { SignupForm } from '@/components/onboarding/SignupForm';
import { SignupsPaused } from '@/components/public/SignupsPaused';
import { publicGet } from '@/lib/api/server';
import type { PublicConfig } from '@/lib/api/types';
import { safetyNumbers } from '@/lib/platform-config';
import { BackLink } from '@/components/ui/BackLink';
import { PageHeading } from '@/components/ui/PageHeading';
import { StepIndicator } from '@/components/ui/StepIndicator';

// Rendered per request: the form is replaced by the pause message while sign-ups are paused (EC-26).
export const dynamic = 'force-dynamic';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'signup' });
  return { title: t('metaTitle') };
}

/** P02 Signup (public), step 1 of 4. Needs the eligibility token from P15 (kept in sessionStorage). */
export default async function SignupPage({ params, searchParams }: { params: Promise<{ locale: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, query, config] = await Promise.all([getTranslations('signup'), searchParams, publicGet<PublicConfig>('meta/config')]);
  if (config?.signupsOpen === false) {
    const { emergencyNumber, supportLine } = safetyNumbers(config);
    return (
      <div className="flex flex-col gap-4 pt-2">
        <BackLink href="/" />
        <PageHeading title={t('title')} />
        <SignupsPaused emergencyNumber={emergencyNumber} supportLine={supportLine} />
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href="/start/eligibility" />
      <StepIndicator step={1} total={4} />
      <PageHeading title={t('title')} subtitle={t('subtitle')} />
      <SignupForm restarted={query.restart === '1'} />
    </div>
  );
}
