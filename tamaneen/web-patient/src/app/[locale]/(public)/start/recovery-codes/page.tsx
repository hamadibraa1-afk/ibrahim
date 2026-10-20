import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { RecoveryCodesPanel } from '@/components/onboarding/RecoveryCodesPanel';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'recoveryCodes' });
  return { title: t('metaTitle') };
}

/** P17 Recovery codes. Public route, but the session cookie is already set by P16. */
export default async function RecoveryCodesPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  return (
    <div className="flex flex-col gap-4 pt-2">
      <RecoveryCodesPanel />
    </div>
  );
}
