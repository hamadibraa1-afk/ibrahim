import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { AppointmentActions } from '@/components/appointments/AppointmentActions';
import { DetailRows } from '@/components/appointments/DetailRows';
import { JoinSessionCard } from '@/components/appointments/JoinSessionCard';
import { isCancelled, sessionLabel, statusLabel } from '@/components/appointments/labels';
import { asLocale, genderKey, timeZoneOf, type Translator } from '@/components/matching/labels';
import { BackLink } from '@/components/ui/BackLink';
import { Card } from '@/components/ui/Card';
import { redirect } from '@/i18n/navigation';
import { getMe, serverGetOptional } from '@/lib/api/server';
import type { AppointmentDetail } from '@/lib/api/types';
import { formatDateTime, formatNumber } from '@/lib/format';

type Params = Promise<{ locale: string; id: string }>;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'appointmentDetail' });
  return { title: t('metaTitle') };
}

/** P28 Appointment detail: when and with whom, the session link, and change or cancel. */
export default async function AppointmentDetailPage({ params }: { params: Params }) {
  const { locale: rawLocale, id } = await params;
  setRequestLocale(rawLocale);
  const locale = asLocale(rawLocale);
  const [t, tList, me, appointment] = await Promise.all([
    getTranslations('appointmentDetail'),
    getTranslations('appointments'),
    getMe(),
    serverGetOptional<AppointmentDetail>(`appointments/${encodeURIComponent(id)}`),
  ]);
  if (!appointment) notFound();
  // P67 and P63 explain what happened and offer rebooking.
  if (appointment.cancelledBy === 'therapist') return redirect({ href: `/appointments/${appointment.id}/cancelled`, locale });
  if (appointment.missed) return redirect({ href: `/appointments/${appointment.id}/missed`, locale });
  const timeZone = timeZoneOf(me);
  const tl: Translator = tList;
  const { therapist } = appointment;
  const statusTone = appointment.status === 'confirmed' ? 'text-primary' : isCancelled(appointment.status) ? 'text-help' : 'text-muted';
  const status = [statusLabel(tl, appointment), appointment.rescheduled ? tl('rescheduled') : null].filter(Boolean).join(' · ');

  return (
    <div className="flex flex-col gap-5 pt-2">
      <BackLink href="/appointments" />
      <h1 className="text-2xl font-bold leading-snug">{sessionLabel(tl, appointment.sequenceNumber, locale)}</h1>

      <DetailRows
        label={t('detailsLabel')}
        rows={[
          { label: t('date'), value: formatDateTime(appointment.startsAt, locale, timeZone) },
          { label: t(genderKey('therapist', therapist.gender)), value: therapist.displayName },
          { label: t('type'), value: t('typeValue', { minutes: formatNumber(appointment.durationMinutes, locale) }) },
          { label: t('status'), value: status, valueClassName: statusTone },
        ]}
      />

      {appointment.status === 'confirmed' ? (
        <>
          <JoinSessionCard joinUrl={appointment.joinUrl} joinAvailableAt={appointment.joinAvailableAt} timeZone={timeZone} />
          <Card className="border border-line text-base leading-relaxed">
            <p>{t.rich('prep', { b: (chunks) => <b>{chunks}</b> })}</p>
          </Card>
        </>
      ) : null}

      <div className="mt-4">
        <AppointmentActions appointment={appointment} timeZone={timeZone} />
      </div>
    </div>
  );
}
