import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { SafetyCheck } from '@/components/onboarding/SafetyCheck';
import { publicGet, serverGet } from '@/lib/api/server';
import type { PublicConfig, ScreeningForm } from '@/lib/api/types';
import { publicPlatformConfig } from '@/lib/platform-config';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'safetyCheck' });
  return { title: t('metaTitle') };
}

/** P23 Safety check (signed in). Questions and the note arrive localized from the API. */
export default async function SafetyCheckPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [form, config] = await Promise.all([serverGet<ScreeningForm>('screening/cssrs'), publicGet<PublicConfig>('meta/config')]);
  const emergencyNumber = config?.emergencyNumber || publicPlatformConfig().emergencyNumber;
  return (
    <div className="pt-4">
      <SafetyCheck form={form} emergencyNumber={emergencyNumber} />
    </div>
  );
}
