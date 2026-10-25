import { useTranslations } from 'next-intl';
import type { Locale } from '@/i18n/routing';
import { Link } from '@/i18n/navigation';
import type { AppointmentDetail, HomeSummary, IntakeStatus } from '@/lib/api/types';
import { formatDate, formatRelativeDays, formatTime } from '@/lib/format';
import { Avatar } from '@/components/ui/Avatar';
import { ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { pluralKey } from '@/components/homework/plural';
import { hourIn } from '@/components/settings/time';
import { HomeJoinButton } from './HomeJoinButton';
import { MoodFaces } from './MoodFaces';

function Chevron() {
  return (
    <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="shrink-0 rtl:rotate-180">
      <path d="M9 6l6 6-6 6" />
    </svg>
  );
}

/**
 * P09 Home. Rendered on the server from GET /v1/home; the mood faces and the join button are interactive.
 * An open (active or paused) intake shows "Continue intake" in place of the session card (PB-46). The next
 * session gets a join button from ten minutes before it starts (PB-43), when `join` is passed.
 */
export function HomeView({
  home,
  locale,
  timeZone,
  now,
  intakeStatus = 'none',
  join = null,
}: {
  home: HomeSummary;
  locale: Locale;
  timeZone: string;
  now: Date;
  intakeStatus?: IntakeStatus;
  join?: Pick<AppointmentDetail, 'joinUrl' | 'joinAvailableAt'> | null;
}) {
  const t = useTranslations('home');
  const hour = hourIn(now, timeZone);
  const greeting = hour >= 4 && hour < 12 ? t('greetingMorning') : t('greetingEvening');
  const appt = home.nextAppointment;
  const homework = home.todayHomework;
  const triage = home.triage;
  const intakeOpen = intakeStatus === 'active' || intakeStatus === 'paused';
  const when = appt
    ? `${formatDate(appt.startsAt, locale, timeZone, { weekday: 'long' })} ${formatTime(appt.startsAt, locale, timeZone)} · ${formatRelativeDays(appt.startsAt, locale, now, timeZone)}`
    : '';

  return (
    <div className="flex flex-col gap-3 pt-4">
      <div>
        <p className="text-sm text-muted">{greeting}</p>
        <h1 className="text-xl font-bold">{home.nickname}</h1>
      </div>

      {triage.suggestionsReady ? (
        <Link href="/matches" className="flex items-center gap-3 rounded-card bg-tint-teal p-4 text-ink">
          <span className="flex flex-1 flex-col gap-1">
            <span className="font-bold text-primary-strong">{t('matchesReady')}</span>
            <span className="text-sm">{t('matchesReadyBody')}</span>
          </span>
          <Chevron />
        </Link>
      ) : triage.status === 'pending' ? (
        <Link href="/waiting" className="flex items-center gap-3 rounded-card bg-tint-sand p-4 text-ink">
          <span className="flex flex-1 flex-col gap-1">
            <span className="font-bold">{t('triagePending')}</span>
            <span className="text-sm">{t('triagePendingBody')}</span>
            {triage.expectedBy ? <span className="text-xs font-semibold">{t('triageExpected', { date: formatDate(triage.expectedBy, locale, timeZone) })}</span> : null}
          </span>
          <Chevron />
        </Link>
      ) : null}

      <MoodFaces initialScore={home.moodToday} />

      {intakeOpen ? (
        <Link href="/intake" data-testid="continue-intake" className="flex items-center gap-3 rounded-card bg-primary p-4 text-surface">
          <span className="flex flex-1 flex-col gap-1">
            <span className="font-bold">{t('continueIntake')}</span>
            <span className="text-sm opacity-90">{t('continueIntakeBody')}</span>
          </span>
          <Chevron />
        </Link>
      ) : null}

      {appt ? (
        <section aria-label={t('nextSession', { name: appt.therapist.displayName })} className="flex flex-col gap-3 rounded-card bg-primary p-4 text-surface">
          <div className="flex items-center gap-3.5">
            <span aria-hidden="true" className="flex h-[52px] w-[52px] shrink-0 flex-col items-center justify-center rounded-[16px] bg-surface/15">
              <span className="text-[11px] font-semibold">{formatDate(appt.startsAt, locale, timeZone, { month: 'long' })}</span>
              <span className="text-xl font-bold leading-none">{formatDate(appt.startsAt, locale, timeZone, { day: 'numeric' })}</span>
            </span>
            <span className="flex min-w-0 flex-1 flex-col">
              <span className="font-bold">{t('nextSession', { name: appt.therapist.displayName })}</span>
              <span className="text-sm opacity-90">
                {when}
              </span>
            </span>
            <Link
              href={`/appointments/${appt.id}`}
              aria-label={t('detailsLabel', { name: appt.therapist.displayName })}
              className="flex min-h-touch shrink-0 items-center rounded-full bg-surface px-4 text-sm font-semibold text-primary"
            >
              {t('details')}
            </Link>
          </div>
          {join ? <HomeJoinButton joinUrl={join.joinUrl} joinAvailableAt={join.joinAvailableAt} name={appt.therapist.displayName} /> : null}
        </section>
      ) : intakeOpen ? null : (
        <Card className="flex flex-col items-start gap-2">
          <h2 className="font-bold">{t('noSession')}</h2>
          <p className="text-sm text-muted">{t('noSessionBody')}</p>
          <ButtonLink href="/therapists" variant="secondary" compact>
            {t('findTherapist')}
          </ButtonLink>
        </Card>
      )}

      {home.latestFeedback ? (
        <Card className="flex items-center gap-3">
          <Avatar initials={home.latestFeedback.initials} size={40} />
          <div className="min-w-0 flex-1">
            <h2 className="text-sm font-semibold">{t('feedbackFrom', { name: home.latestFeedback.therapistName })}</h2>
            <p className="truncate text-sm text-muted">{home.latestFeedback.text}</p>
          </div>
        </Card>
      ) : null}

      <div className="grid grid-cols-2 gap-2.5">
        {homework ? (
          <Link href={`/homework/${homework.id}`} className="flex flex-col gap-1.5 rounded-[20px] bg-tint-sand p-3.5 text-ink">
            <span className="text-[15px] font-bold">{t('todayHomework')}</span>
            <span className="text-sm">
              {homework.minutes === null ? homework.title : `${homework.title} · ${t(`minutes.${pluralKey(locale, homework.minutes)}`, { n: homework.minutes })}`}
            </span>
            <span className="text-xs font-bold">{t(`homeworkProgress.${pluralKey(locale, homework.totalDays)}`, { done: homework.doneDays, total: homework.totalDays })}</span>
          </Link>
        ) : null}
        <Link href="/safety-plan" className={`flex flex-col gap-1.5 rounded-[20px] bg-tint-teal p-3.5 text-ink ${homework ? '' : 'col-span-2'}`}>
          <span className="text-[15px] font-bold">{t('safetyPlan')}</span>
          <span className="text-sm text-primary-strong">{t('safetyPlanBody')}</span>
          <span className="text-xs font-bold text-primary-strong">{t('safetyPlanOffline')}</span>
        </Link>
      </div>
    </div>
  );
}
