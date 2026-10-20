import { useLocale, useTranslations } from 'next-intl';
import { Link } from '@/i18n/navigation';
import type { Appointment, AppointmentList } from '@/lib/api/types';
import { formatDate, formatTime } from '@/lib/format';
import { Tag } from '@/components/ui/Chip';
import { EmptyState } from '@/components/ui/EmptyState';
import { asLocale, type Translator } from '@/components/matching/labels';
import { isCancelled, sessionLabel, statusLabel } from './labels';

const tagTone = { confirmed: 'teal', completed: 'muted', no_show: 'sand' } as const;

function AppointmentRow({ appointment, highlight, past, timeZone }: { appointment: Appointment; highlight: boolean; past: boolean; timeZone: string }) {
  const t: Translator = useTranslations('appointments');
  const locale = asLocale(useLocale());
  const cancelled = isCancelled(appointment.status);
  const { startsAt, therapist } = appointment;

  const title = cancelled
    ? t('cancelledTitle')
    : `${formatDate(startsAt, locale, timeZone, { weekday: 'long' })} · ${formatTime(startsAt, locale, timeZone)}`;
  const subtitle = (
    cancelled
      ? [statusLabel(t, appointment), appointment.rescheduled ? t('rescheduled') : null]
      : [therapist.displayName, sessionLabel(t, appointment.sequenceNumber, locale)]
  )
    .filter(Boolean)
    .join(' · ');
  const tone = cancelled ? null : tagTone[appointment.status as keyof typeof tagTone];

  return (
    <li>
      <Link
        href={`/appointments/${encodeURIComponent(appointment.id)}`}
        className={`flex items-center gap-4 rounded-card bg-surface p-4 ${highlight ? 'border-[1.5px] border-primary' : 'border border-line'}`}
      >
        <span aria-hidden="true" className="flex w-14 shrink-0 flex-col items-center">
          <span className={`text-xs font-semibold ${past ? 'text-muted' : 'text-primary'}`}>{formatDate(startsAt, locale, timeZone, { month: 'long' })}</span>
          <span className={`text-2xl font-bold ${past ? 'text-muted' : 'text-ink'}`}>{formatDate(startsAt, locale, timeZone, { day: 'numeric' })}</span>
        </span>
        <span className="sr-only">{formatDate(startsAt, locale, timeZone)}</span>
        <span className="flex min-w-0 flex-1 flex-col gap-1">
          <span className="text-base font-bold">{title}</span>
          <span className="text-sm text-muted">{subtitle}</span>
        </span>
        {tone ? <Tag tone={tone}>{statusLabel(t, appointment)}</Tag> : null}
      </Link>
    </li>
  );
}

/** P27 lists: upcoming (the next one highlighted) and past, each a list of links to P28. */
export function AppointmentListView({ list, timeZone }: { list: AppointmentList; timeZone: string }) {
  const t = useTranslations('appointments');
  const byTime = (a: Appointment, b: Appointment) => Date.parse(a.startsAt) - Date.parse(b.startsAt);
  const upcoming = [...list.upcoming].sort(byTime);
  const past = [...list.past].sort((a, b) => byTime(b, a));

  return (
    <div className="flex flex-col gap-6">
      <section aria-labelledby="upcoming-heading" className="flex flex-col gap-3">
        <h2 id="upcoming-heading" className="text-sm font-bold text-muted">
          {t('upcoming')}
        </h2>
        {upcoming.length === 0 ? (
          <EmptyState title={t('emptyUpcoming')} body={t('emptyUpcomingBody')} />
        ) : (
          <ul className="flex flex-col gap-3">
            {upcoming.map((a, i) => (
              <AppointmentRow key={a.id} appointment={a} highlight={i === 0 && a.status === 'confirmed'} past={false} timeZone={timeZone} />
            ))}
          </ul>
        )}
      </section>

      {past.length > 0 ? (
        <section aria-labelledby="past-heading" className="flex flex-col gap-3">
          <h2 id="past-heading" className="text-sm font-bold text-muted">
            {t('past')}
          </h2>
          <ul className="flex flex-col gap-3">
            {past.map((a) => (
              <AppointmentRow key={a.id} appointment={a} highlight={false} past timeZone={timeZone} />
            ))}
          </ul>
        </section>
      ) : null}
    </div>
  );
}
