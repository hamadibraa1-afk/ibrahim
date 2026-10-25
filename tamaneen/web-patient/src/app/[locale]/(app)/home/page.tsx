import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { HomeView } from '@/components/home/HomeView';
import { BottomNav, BottomNavSpacer } from '@/components/ui/BottomNav';
import { isLocale, routing } from '@/i18n/routing';
import { getMe, serverGet, serverGetOptional } from '@/lib/api/server';
import type { AppointmentDetail, HomeSummary } from '@/lib/api/types';
import { DEFAULT_TIME_ZONE } from '@/lib/format';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'home' });
  return { title: t('metaTitle') };
}

/** The join link only matters for a session starting within a day; the button itself appears 10 minutes before. */
const JOIN_LOOKAHEAD_MS = 24 * 60 * 60 * 1000;

/** P09 Home: mood in one tap, the next session, the latest note, today's homework and the safety plan. */
export default async function HomePage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [home, me] = await Promise.all([serverGet<HomeSummary>('home'), getMe()]);
  const now = new Date();
  const next = home.nextAppointment;
  const join = next && Date.parse(next.startsAt) - now.getTime() <= JOIN_LOOKAHEAD_MS ? await serverGetOptional<AppointmentDetail>(`appointments/${next.id}`).catch(() => null) : null;
  return (
    <>
      <HomeView
        home={home}
        locale={isLocale(locale) ? locale : routing.defaultLocale}
        timeZone={me.timeZone || DEFAULT_TIME_ZONE}
        now={now}
        intakeStatus={me.onboarding.intakeStatus}
        join={join}
      />
      <BottomNavSpacer />
      <BottomNav active="home" />
    </>
  );
}
