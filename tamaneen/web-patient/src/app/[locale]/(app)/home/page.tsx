import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { CareEndedCard } from '@/components/care/CareEndedCard';
import { HomeView } from '@/components/home/HomeView';
import { Link, redirect } from '@/i18n/navigation';
import { BottomNav, BottomNavSpacer } from '@/components/ui/BottomNav';
import { isLocale, routing } from '@/i18n/routing';
import { getMe, serverGet, serverGetOptional } from '@/lib/api/server';
import type { AppointmentDetail, CrisisFollowUp, HomeSummary } from '@/lib/api/types';
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
  const [home, me, followUp] = await Promise.all([
    serverGet<HomeSummary>('home'),
    getMe(),
    serverGetOptional<CrisisFollowUp>('crisis/follow-up').catch(() => null),
  ]);
  // P38: after a clinician closed a crisis event, the next visit home opens the follow-up until it is seen.
  if (followUp?.state === 'closed' && followUp.unread) redirect({ href: '/crisis/after', locale: isLocale(locale) ? locale : routing.defaultLocale });
  const tAfter = await getTranslations('afterCrisis');
  const now = new Date();
  const next = home.nextAppointment;
  const join = next && Date.parse(next.startsAt) - now.getTime() <= JOIN_LOOKAHEAD_MS ? await serverGetOptional<AppointmentDetail>(`appointments/${next.id}`).catch(() => null) : null;
  return (
    <>
      {followUp?.state === 'closed' ? (
        <Link href="/crisis/after" data-testid="after-crisis-link" className="mt-3 flex min-h-touch items-center justify-between rounded-card bg-tint-teal px-4 py-3 font-semibold text-primary-strong">
          {tAfter('homeCard')}
          <span aria-hidden="true" className="rtl:rotate-180">→</span>
        </Link>
      ) : null}
      {me.careClosedAt ? <CareEndedCard /> : null}
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
