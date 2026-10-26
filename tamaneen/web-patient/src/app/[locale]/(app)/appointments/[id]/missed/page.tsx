import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { MissedCheckIn } from '@/components/appointments/MissedCheckIn';
import { TalkNowButton } from '@/components/appointments/TalkNowButton';
import { asLocale, timeZoneOf } from '@/components/matching/labels';
import { BackLink } from '@/components/ui/BackLink';
import { ButtonLink } from '@/components/ui/Button';
import { redirect } from '@/i18n/navigation';
import { getMe, serverGetOptional } from '@/lib/api/server';
import type { AppointmentDetail } from '@/lib/api/types';
import { formatDate } from '@/lib/format';

type Params = Promise<{ locale: string; id: string }>;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'appointmentDetail.missed' });
  return { title: t('metaTitle') };
}

/**
 * P63 Missed session: no blame, nothing deducted or counted (§11.1), how are you now, and rebooking with the same
 * therapist or talking to someone at once. The therapist's note card is hidden: the API has no such note yet.
 */
export default async function MissedSessionPage({ params }: { params: Params }) {
  const { locale: rawLocale, id } = await params;
  setRequestLocale(rawLocale);
  const locale = asLocale(rawLocale);
  const [t, me, appointment] = await Promise.all([
    getTranslations('appointmentDetail.missed'),
    getMe(),
    serverGetOptional<AppointmentDetail>(`appointments/${encodeURIComponent(id)}`),
  ]);
  if (!appointment) notFound();
  if (!appointment.missed) return redirect({ href: `/appointments/${appointment.id}`, locale });
  const timeZone = timeZoneOf(me);
  const sameDay = formatDate(appointment.startsAt, locale, timeZone) === formatDate(new Date().toISOString(), locale, timeZone);

  return (
    <div className="flex flex-col gap-4 pt-2" data-testid="missed-session">
      <BackLink href="/home" />
      <div className="flex flex-col items-center gap-2 pt-3 text-center">
        <svg width="110" height="90" viewBox="0 0 110 90" aria-hidden="true">
          <ellipse cx="55" cy="82" rx="40" ry="6" className="fill-tint-sand" />
          <circle cx="55" cy="40" r="32" className="fill-tint-sand" />
          <path d="M55 22v20l12 8" className="stroke-ink" strokeWidth="5" fill="none" strokeLinecap="round" />
        </svg>
        <h1 className="text-2xl font-bold leading-snug">
          {sameDay ? t('titleToday') : t('title', { date: formatDate(appointment.startsAt, locale, timeZone, { weekday: 'long', day: 'numeric', month: 'long' }) })}
        </h1>
        <p className="text-base leading-relaxed text-muted">{t('reassure')}</p>
      </div>

      <MissedCheckIn />

      <div className="mt-2 flex flex-col gap-3">
        <ButtonLink href={`/therapists/${encodeURIComponent(appointment.therapist.id)}/book`} full data-testid="rebook">
          {t('rebook')}
        </ButtonLink>
        <TalkNowButton label={t('talkNow')} />
      </div>
    </div>
  );
}
