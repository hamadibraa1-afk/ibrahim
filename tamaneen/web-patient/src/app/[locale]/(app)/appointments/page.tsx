import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { AppointmentListView } from '@/components/appointments/AppointmentListView';
import { timeZoneOf } from '@/components/matching/labels';
import { BottomNav, BottomNavSpacer } from '@/components/ui/BottomNav';
import { PageHeading } from '@/components/ui/PageHeading';
import { Link } from '@/i18n/navigation';
import { getMe, serverGet } from '@/lib/api/server';
import type { AppointmentList } from '@/lib/api/types';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'appointments' });
  return { title: t('metaTitle') };
}

/** P27 My appointments. */
export default async function AppointmentsPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, me, list] = await Promise.all([getTranslations('appointments'), getMe(), serverGet<AppointmentList>('appointments')]);

  return (
    <>
      <div className="flex flex-col gap-5 pt-4">
        <PageHeading title={t('title')} />
        <AppointmentListView list={list} timeZone={timeZoneOf(me)} />
        <Link href="/therapists" className="mt-6 self-center py-3 text-base font-semibold text-primary underline underline-offset-4">
          {t('bookNew')}
        </Link>
      </div>
      <BottomNavSpacer />
      <BottomNav active="appointments" />
    </>
  );
}
