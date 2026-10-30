import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { BookingFlow } from '@/components/booking/BookingFlow';
import { isNotEligible, toShownProblem, type ShownProblem } from '@/components/booking/problem';
import { asLocale, timeZoneOf } from '@/components/matching/labels';
import { Avatar } from '@/components/ui/Avatar';
import { BackLink } from '@/components/ui/BackLink';
import { Notice } from '@/components/ui/Notice';
import { getMe, serverGet, serverGetOptional } from '@/lib/api/server';
import type { AppointmentDetail, Slot, TherapistProfile } from '@/lib/api/types';
import { formatDateTime, formatNumber } from '@/lib/format';

type Params = Promise<{ locale: string; id: string }>;
type SearchParams = Promise<Record<string, string | string[] | undefined>>;

const ID_PATTERN = /^[A-Za-z0-9._~-]{1,64}$/;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'booking' });
  return { title: t('metaTitle') };
}

async function loadSlots(therapistId: string, fallback: string): Promise<{ slots: Slot[]; notEligible: ShownProblem | null }> {
  try {
    return { slots: await serverGet<Slot[]>(`therapists/${encodeURIComponent(therapistId)}/slots?days=14`), notEligible: null };
  } catch (error) {
    if (isNotEligible(error)) return { slots: [], notEligible: toShownProblem(error, fallback) };
    throw error;
  }
}

/**
 * P08 Booking (and the reschedule mode reached from P28 with ?reschedule={appointmentId}). From P67 the same mode
 * rebooks the session the therapist cancelled, with priority: same session number, no 24-hour limit.
 */
export default async function BookPage({ params, searchParams }: { params: Params; searchParams: SearchParams }) {
  const { locale: rawLocale, id } = await params;
  setRequestLocale(rawLocale);
  const locale = asLocale(rawLocale);
  const query = await searchParams;
  const rawReschedule = Array.isArray(query.reschedule) ? query.reschedule[0] : query.reschedule;
  const rescheduleId = rawReschedule && ID_PATTERN.test(rawReschedule) ? rawReschedule : null;

  const t = await getTranslations('booking');
  const [me, therapist, { slots, notEligible }, current] = await Promise.all([
    getMe(),
    serverGetOptional<TherapistProfile>(`therapists/${encodeURIComponent(id)}`),
    loadSlots(id, t('notEligibleFallback')),
    rescheduleId ? serverGetOptional<AppointmentDetail>(`appointments/${encodeURIComponent(rescheduleId)}`) : Promise.resolve(null),
  ]);
  if (!therapist) notFound();
  const timeZone = timeZoneOf(me);
  // Reschedule only an appointment that exists and is with this therapist; otherwise this is a new booking.
  const reschedule = current && current.therapist.id === therapist.id ? current : null;

  return (
    <div className="flex flex-col gap-5 pt-2">
      <BackLink href={reschedule ? `/appointments/${encodeURIComponent(reschedule.id)}` : `/therapists/${encodeURIComponent(therapist.id)}`} />

      {reschedule ? <p className="text-sm font-semibold text-primary">{t('rescheduleEyebrow')}</p> : null}

      <header className="flex items-center gap-3 rounded-card border border-line bg-surface p-4">
        <Avatar initials={therapist.initials} size={56} />
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="text-lg font-bold">{therapist.displayName}</h1>
          <p className="text-sm text-muted">{t('sessionLine', { minutes: formatNumber(therapist.durationMinutes, locale) })}</p>
        </div>
      </header>

      {reschedule ? (
        <Notice tone="info">
          {reschedule.cancelledBy === 'therapist'
            ? t('reschedulePriority', { date: formatDateTime(reschedule.startsAt, locale, timeZone) })
            : t('rescheduleCurrent', { date: formatDateTime(reschedule.startsAt, locale, timeZone) })}
        </Notice>
      ) : null}

      <BookingFlow therapistId={therapist.id} initialSlots={slots} timeZone={timeZone} rescheduleId={reschedule?.id ?? null} notEligible={notEligible} />
    </div>
  );
}
