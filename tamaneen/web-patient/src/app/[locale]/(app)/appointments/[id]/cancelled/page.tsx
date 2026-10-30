import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { TalkNowButton } from '@/components/appointments/TalkNowButton';
import { asLocale, genderKey, timeZoneOf } from '@/components/matching/labels';
import { BackLink } from '@/components/ui/BackLink';
import { ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { redirect } from '@/i18n/navigation';
import { getMe, serverGetOptional } from '@/lib/api/server';
import type { AppointmentDetail } from '@/lib/api/types';
import { formatDateTime } from '@/lib/format';

type Params = Promise<{ locale: string; id: string }>;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'appointmentDetail.therapistCancelled' });
  return { title: t('metaTitle') };
}

/**
 * P67 The therapist cancelled. An apology with no reason beyond «لظرف طارئ» (the real reason is never shown), then
 * rebooking: a priority reschedule of this same appointment with the same therapist (same session number, no 24-hour
 * limit), or the suggested therapists. The same-time substitute card is deliberately absent: it needs S65 (phase 8),
 * so the design's "no substitute" state applies and the care team helps rebook within 48 hours (EC-52). The session is
 * not counted.
 */
export default async function TherapistCancelledPage({ params }: { params: Params }) {
  const { locale: rawLocale, id } = await params;
  setRequestLocale(rawLocale);
  const locale = asLocale(rawLocale);
  const [t, me, appointment] = await Promise.all([
    getTranslations('appointmentDetail.therapistCancelled'),
    getMe(),
    serverGetOptional<AppointmentDetail>(`appointments/${encodeURIComponent(id)}`),
  ]);
  if (!appointment) notFound();
  if (appointment.cancelledBy !== 'therapist') return redirect({ href: `/appointments/${appointment.id}`, locale });
  const { therapist } = appointment;
  const when = formatDateTime(appointment.startsAt, locale, timeZoneOf(me));

  return (
    <div className="flex flex-col gap-4 pt-2" data-testid="therapist-cancelled">
      <BackLink href="/home" />
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-bold leading-snug">{t(genderKey('title', therapist.gender), { name: therapist.displayName })}</h1>
        <p className="text-sm text-muted">
          {t('when', { when })}
        </p>
        <p className="text-base leading-relaxed text-muted">{t('reason')}</p>
      </div>

      <Card className="flex flex-col gap-3">
        <h2 className="text-sm font-semibold text-muted">{t('options')}</h2>
        <ButtonLink href={`/therapists/${encodeURIComponent(therapist.id)}/book?reschedule=${encodeURIComponent(appointment.id)}`} full data-testid="rebook-same">
          {t(genderKey('rebookSame', therapist.gender), { name: therapist.displayName })}
        </ButtonLink>
        <p className="text-sm text-primary" data-testid="rebook-priority">{t(genderKey('priority', therapist.gender))}</p>
        <p className="text-xs text-muted">{t('rebookHelp')}</p>
        <ButtonLink href="/matches" variant="secondary" full data-testid="rebook-other">
          {t('rebookOther')}
        </ButtonLink>
      </Card>

      <TalkNowButton label={t('talkNow')} />
      <p className="text-center text-xs leading-relaxed text-muted">{t('notCounted')}</p>
    </div>
  );
}
