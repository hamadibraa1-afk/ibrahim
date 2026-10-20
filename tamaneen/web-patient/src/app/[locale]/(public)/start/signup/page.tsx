import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { SignupForm } from '@/components/onboarding/SignupForm';
import { BackLink } from '@/components/ui/BackLink';
import { PageHeading } from '@/components/ui/PageHeading';
import { StepIndicator } from '@/components/ui/StepIndicator';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'signup' });
  return { title: t('metaTitle') };
}

/** P02 Signup (public), step 1 of 4. Needs the eligibility token from P15 (kept in sessionStorage). */
export default async function SignupPage({ params, searchParams }: { params: Promise<{ locale: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, query] = await Promise.all([getTranslations('signup'), searchParams]);
  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href="/start/eligibility" />
      <StepIndicator step={1} total={4} />
      <PageHeading title={t('title')} subtitle={t('subtitle')} />
      <SignupForm restarted={query.restart === '1'} />
    </div>
  );
}
