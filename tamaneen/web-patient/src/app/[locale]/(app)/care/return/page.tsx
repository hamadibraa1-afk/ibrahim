import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { SafetyCheck } from '@/components/onboarding/SafetyCheck';
import { BackLink } from '@/components/ui/BackLink';
import { Notice } from '@/components/ui/Notice';
import { asLocale } from '@/components/matching/labels';
import { redirect } from '@/i18n/navigation';
import { publicGet, serverGet, serverGetOptional } from '@/lib/api/server';
import type { CareClosure, PublicConfig, ScreeningForm } from '@/lib/api/types';
import { publicPlatformConfig } from '@/lib/platform-config';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'care.return' });
  return { title: t('metaTitle') };
}

/**
 * P53 «أحتاج أرجع للجلسات»: the short safety check the design asks for before re-entry. It is P23 itself (the same
 * C-SSRS screener and crisis gate); when it passes, matching reopens with the previous therapist first.
 */
export default async function CareReturnPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const closure = await serverGetOptional<CareClosure>('me/care-closure');
  if (!closure) return redirect({ href: '/care/complete', locale: asLocale(locale) });
  const [t, form, config] = await Promise.all([getTranslations('care.return'), serverGet<ScreeningForm>('screening/cssrs'), publicGet<PublicConfig>('meta/config')]);
  const emergencyNumber = config?.emergencyNumber || publicPlatformConfig().emergencyNumber;
  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href="/care/complete" />
      <Notice tone="info">{t('intro')}</Notice>
      <SafetyCheck form={form} emergencyNumber={emergencyNumber} mode="careReturn" />
    </div>
  );
}
