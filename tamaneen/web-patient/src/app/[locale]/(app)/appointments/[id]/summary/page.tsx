import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { asLocale, timeZoneOf } from '@/components/matching/labels';
import { SessionSummaryView } from '@/components/session/SessionSummaryView';
import { summaryRedirect } from '@/components/session/sessionRoutes';
import { redirect } from '@/i18n/navigation';
import { getMe, serverGet, serverGetOptional } from '@/lib/api/server';
import type { AppointmentDetail, AppointmentList, HomeworkList, SessionFeedback, SessionState } from '@/lib/api/types';

type Params = Promise<{ locale: string; id: string }>;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'session.summary' });
  return { title: t('metaTitle') };
}

/**
 * P43 after the session: next appointment, homework and the private rating. Only for a session that took place:
 * a cancelled one goes to P67 or P28, a missed one to P63, one that has not happened yet to the lobby.
 */
export default async function SessionSummaryPage({ params }: { params: Params }) {
  const { locale, id } = await params;
  setRequestLocale(locale);
  const path = `appointments/${encodeURIComponent(id)}`;
  const appointment = await serverGetOptional<AppointmentDetail>(path);
  if (!appointment) notFound();
  const presence = appointment.status === 'confirmed' ? await serverGetOptional<SessionState>(`${path}/presence`) : null;
  const now = presence ? Date.parse(presence.serverNow) : Date.now();
  const away = summaryRedirect(appointment, presence !== null && (presence.peer.joinedAt !== null || presence.peer.present), now, presence?.closesAt);
  if (away) return redirect({ href: away, locale: asLocale(locale) });
  const [me, list, homework, feedback] = await Promise.all([
    getMe(),
    serverGet<AppointmentList>('appointments'),
    serverGet<HomeworkList>('homework'),
    serverGetOptional<SessionFeedback>(`${path}/feedback`),
  ]);
  const next = list.upcoming.find((a) => a.id !== appointment.id && Date.parse(a.startsAt) > Date.parse(appointment.startsAt)) ?? null;

  return (
    <div className="pt-2">
      <SessionSummaryView appointment={appointment} next={next} homework={homework.active} feedback={feedback} timeZone={timeZoneOf(me)} />
    </div>
  );
}
