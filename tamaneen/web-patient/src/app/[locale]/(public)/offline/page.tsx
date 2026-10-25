import { getTranslations, setRequestLocale } from 'next-intl/server';
import { EmergencyLines } from '@/components/pwa/EmergencyLines';
import { RetryButton } from '@/components/pwa/RetryButton';
import { StatePanel } from '@/components/pwa/StatePanel';

// Rendered per request and precached by the service worker, which serves it when a page can't load offline.
export const dynamic = 'force-dynamic';

/** P35 Offline. */
export default async function OfflinePage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations('systemStates.offline');

  return (
    <StatePanel icon="offline" title={t('title')} body={t('body')}>
      <EmergencyLines />
      <RetryButton label={t('retry')} />
    </StatePanel>
  );
}
