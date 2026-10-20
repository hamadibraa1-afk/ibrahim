import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { LoginForm } from '@/components/auth/LoginForm';
import { PageHeading } from '@/components/ui/PageHeading';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'login' });
  return { title: t('metaTitle') };
}

/** P18 Login (public). ?reset=1 after a password reset (P20), ?restart=1 when the MFA step was lost. */
export default async function LoginPage({ params, searchParams }: { params: Promise<{ locale: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, query] = await Promise.all([getTranslations('login'), searchParams]);
  const notice = query.reset === '1' ? 'reset' : query.restart === '1' ? 'restart' : null;
  return (
    <div className="flex flex-col gap-6 pt-8">
      <PageHeading title={t('title')} />
      <LoginForm notice={notice} />
    </div>
  );
}
