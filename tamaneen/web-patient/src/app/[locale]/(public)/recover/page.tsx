import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { RecoverForm } from '@/components/auth/RecoverForm';
import { BackLink } from '@/components/ui/BackLink';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'recover' });
  return { title: t('metaTitle') };
}

/** P20 Recover account (public). */
export default async function RecoverPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href="/login" />
      <RecoverForm />
    </div>
  );
}
