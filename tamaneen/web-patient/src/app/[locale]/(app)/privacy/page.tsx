import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { AccessLog } from '@/components/privacy/AccessLog';
import { ConsentsSection } from '@/components/privacy/ConsentsSection';
import { MyData } from '@/components/privacy/MyData';
import { BackLink } from '@/components/ui/BackLink';
import { PageHeading } from '@/components/ui/PageHeading';
import { getMe, serverGet } from '@/lib/api/server';
import type { AccessLogEntry, Consent, DataRequestStatus } from '@/lib/api/types';
import { DEFAULT_TIME_ZONE } from '@/lib/format';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'privacy' });
  return { title: t('metaTitle') };
}

/** P11 Privacy and my data: consents, who opened the file, export and account deletion. */
export default async function PrivacyPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [consents, accessLog, dataExport, me, t] = await Promise.all([
    serverGet<Consent[]>('me/consents'),
    serverGet<AccessLogEntry[]>('me/access-log'),
    serverGet<DataRequestStatus>('me/data-export'),
    getMe(),
    getTranslations('privacy'),
  ]);
  const timeZone = me.timeZone || DEFAULT_TIME_ZONE;
  return (
    <div className="flex flex-col gap-6 pt-2">
      <div className="flex flex-col gap-2">
        <BackLink href="/settings" label={t('back')} />
        <PageHeading title={t('title')} />
      </div>
      <ConsentsSection initial={consents} />
      <AccessLog entries={accessLog} nowIso={new Date().toISOString()} timeZone={timeZone} />
      <MyData initialExport={dataExport} timeZone={timeZone} />
    </div>
  );
}
