import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { CheckInForm } from '@/components/mood/CheckInForm';
import { BackLink } from '@/components/ui/BackLink';
import { PageHeading } from '@/components/ui/PageHeading';
import { getMe, serverGet } from '@/lib/api/server';
import type { MoodEntry } from '@/lib/api/types';
import { DEFAULT_TIME_ZONE } from '@/lib/format';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'checkin' });
  return { title: t('metaTitle') };
}

/** P30 Mood check-in. */
export default async function CheckInPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [entries, me, t] = await Promise.all([serverGet<MoodEntry[]>('mood?days=7'), getMe(), getTranslations('checkin')]);
  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href="/home" />
      <PageHeading title={t('title')} />
      <CheckInForm initialEntries={entries} nowIso={new Date().toISOString()} timeZone={me.timeZone || DEFAULT_TIME_ZONE} />
    </div>
  );
}
