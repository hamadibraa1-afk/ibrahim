import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { NotificationsForm } from '@/components/settings/NotificationsForm';
import { BackLink } from '@/components/ui/BackLink';
import { PageHeading } from '@/components/ui/PageHeading';
import { serverGet } from '@/lib/api/server';
import type { NotificationPrefs } from '@/lib/api/types';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'settings.notifications' });
  return { title: t('title') };
}

/** P33 Notifications. */
export default async function NotificationsPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [prefs, t, ts] = await Promise.all([serverGet<NotificationPrefs>('me/notifications'), getTranslations('settings.notifications'), getTranslations('settings')]);
  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href="/settings" label={ts('back')} />
      <PageHeading title={t('title')} />
      <NotificationsForm initial={prefs} />
    </div>
  );
}
