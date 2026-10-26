import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { timeZoneOf } from '@/components/matching/labels';
import { SessionSummaryView } from '@/components/session/SessionSummaryView';
import { getMe, serverGet, serverGetOptional } from '@/lib/api/server';
import type { AppointmentDetail, AppointmentList, HomeworkList, SessionFeedback } from '@/lib/api/types';

type Params = Promise<{ locale: string; id: string }>;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'session.summary' });
  return { title: t('metaTitle') };
}

/** P43 after the session: next appointment, homework and the private rating. */
export default async function SessionSummaryPage({ params }: { params: Params }) {
  const { locale, id } = await params;
  setRequestLocale(locale);
  const path = `appointments/${encodeURIComponent(id)}`;
  const [me, appointment, list, homework, feedback] = await Promise.all([
    getMe(),
    serverGetOptional<AppointmentDetail>(path),
    serverGet<AppointmentList>('appointments'),
    serverGet<HomeworkList>('homework'),
    serverGetOptional<SessionFeedback>(`${path}/feedback`),
  ]);
  if (!appointment) notFound();
  const next = list.upcoming.find((a) => a.id !== appointment.id && Date.parse(a.startsAt) > Date.parse(appointment.startsAt)) ?? null;

  return (
    <div className="pt-2">
      <SessionSummaryView appointment={appointment} next={next} homework={homework.active} feedback={feedback} timeZone={timeZoneOf(me)} />
    </div>
  );
}
