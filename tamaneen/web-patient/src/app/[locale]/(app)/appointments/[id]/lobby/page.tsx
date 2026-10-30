import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { sessionLabel } from '@/components/appointments/labels';
import { asLocale, type Translator } from '@/components/matching/labels';
import { SessionLobby } from '@/components/session/SessionLobby';
import { notHeldRoute } from '@/components/session/sessionRoutes';
import { BackLink } from '@/components/ui/BackLink';
import { redirect } from '@/i18n/navigation';
import { serverGetOptional } from '@/lib/api/server';
import type { AppointmentDetail, PreSessionNote, SessionState } from '@/lib/api/types';

type Params = Promise<{ locale: string; id: string }>;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'session.lobby' });
  return { title: t('metaTitle') };
}

/** P41 lobby, with P54 (therapist late) as one of its states. */
export default async function SessionLobbyPage({ params }: { params: Params }) {
  const { locale: rawLocale, id } = await params;
  setRequestLocale(rawLocale);
  const locale = asLocale(rawLocale);
  const path = `appointments/${encodeURIComponent(id)}`;
  const [t, tList, appointment, state, note] = await Promise.all([
    getTranslations('session.lobby'),
    getTranslations('appointments'),
    serverGetOptional<AppointmentDetail>(path),
    serverGetOptional<SessionState>(`${path}/presence`),
    serverGetOptional<PreSessionNote>(`${path}/pre-session-note`),
  ]);
  if (!appointment) notFound();
  // P67 / P63: a session the therapist cancelled or marked missed has its own page, never the lobby.
  const away = notHeldRoute(appointment);
  if (away && (appointment.cancelledBy === 'therapist' || appointment.missed)) return redirect({ href: away, locale });

  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href={`/appointments/${encodeURIComponent(appointment.id)}`} />
      <h1 className="text-2xl font-bold leading-snug">{t('title')}</h1>
      <SessionLobby appointment={appointment} initialState={state} initialNote={note?.text ?? ''} sessionLabel={sessionLabel(tList as Translator, appointment.sequenceNumber, locale)} />
    </div>
  );
}
