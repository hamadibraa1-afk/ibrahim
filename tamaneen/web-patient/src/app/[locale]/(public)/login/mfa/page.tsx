import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { MfaForm } from '@/components/auth/MfaForm';
import { BackLink } from '@/components/ui/BackLink';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'loginMfa' });
  return { title: t('metaTitle') };
}

/** P19 Login MFA (public). The MFA token from P18 is kept in sessionStorage for this tab only. */
export default async function LoginMfaPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href="/login" />
      <MfaForm />
    </div>
  );
}
