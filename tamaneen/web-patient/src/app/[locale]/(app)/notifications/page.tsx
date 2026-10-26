import type { Metadata } from 'next';
import { unstable_rethrow } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { NotificationList } from '@/components/notifications/NotificationList';
import { RetryButton } from '@/components/pwa/RetryButton';
import { BottomNav, BottomNavSpacer } from '@/components/ui/BottomNav';
import { PageHeading } from '@/components/ui/PageHeading';
import { ApiError } from '@/lib/api/errors';
import { serverGet } from '@/lib/api/server';
import type { Inbox } from '@/lib/api/types';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'notifications' });
  return { title: t('title') };
}

/** P46 notification centre. A failed load shows the system's fault and the problem code (RFC 7807 `code`) for support. */
export default async function NotificationsPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations('notifications');
  let inbox: Inbox | null = null;
  let code: string | null = null;
  try {
    inbox = await serverGet<Inbox>('me/inbox');
  } catch (e) {
    unstable_rethrow(e);
    code = e instanceof ApiError ? (e.code ?? `HTTP-${e.status}`) : 'NET';
  }

  return (
    <div className="flex flex-col gap-5 pt-4">
      <PageHeading title={t('title')} />
      {inbox ? (
        <NotificationList initial={inbox} />
      ) : (
        <div role="alert" className="flex flex-col items-center gap-3 rounded-card bg-surface px-6 py-10 text-center">
          <p className="text-lg font-bold">{t('errorTitle')}</p>
          <p className="text-sm text-muted">{t('errorBody')}</p>
          <RetryButton label={t('retry')} />
          <p className="text-xs text-muted">
            {t('errorCode')} <bdi dir="ltr">{code}</bdi>
          </p>
        </div>
      )}
      <BottomNavSpacer />
      <BottomNav active="home" />
    </div>
  );
}
