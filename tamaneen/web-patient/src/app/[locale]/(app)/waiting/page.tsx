import { getTranslations, setRequestLocale } from 'next-intl/server';
import type { ReactNode } from 'react';
import { hoursUntil } from '@/components/intake/triage';
import { MatchReadyWatcher } from '@/components/intake/MatchReadyWatcher';
import { WaitingActivities } from '@/components/intake/WaitingActivities';
import { Link, redirect } from '@/i18n/navigation';
import { isLocale, routing } from '@/i18n/routing';
import { getMe, serverGet } from '@/lib/api/server';
import type { TriageState } from '@/lib/api/types';
import { formatRelativeDays, formatTime } from '@/lib/format';

type StepState = 'done' | 'current' | 'pending';

function Step({ state, srLabel, title, detail }: { state: StepState; srLabel: string; title: string; detail: ReactNode }) {
  return (
    <li className="flex items-start gap-4">
      {state === 'done' ? (
        <span aria-hidden="true" className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-primary text-surface">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
            <path d="M5 12l4 4 10-10" />
          </svg>
        </span>
      ) : (
        <span aria-hidden="true" className={`h-11 w-11 shrink-0 rounded-full border-[3px] ${state === 'current' ? 'border-primary' : 'border-line'}`} />
      )}
      <div className="flex flex-col gap-0.5 pt-1">
        <p className={`text-lg font-bold ${state === 'pending' ? 'text-muted' : ''}`}>
          <span className="sr-only">{srLabel}</span>
          {title}
        </p>
        <div className={`text-sm ${state === 'current' ? 'font-semibold text-primary' : 'text-muted'}`}>{detail}</div>
      </div>
    </li>
  );
}

/** P36 Waiting for review: what happened, what happens next, and something calming to do meanwhile. */
export default async function WaitingPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale: raw } = await params;
  const locale = isLocale(raw) ? raw : routing.defaultLocale;
  setRequestLocale(locale);
  const [t, me, triage] = await Promise.all([getTranslations('waitingTriage'), getMe(), serverGet<TriageState>('triage')]);
  if (triage.status === 'none') return redirect({ href: '/intake/summary', locale });

  const reviewed = triage.status === 'reviewed' || triage.status === 'matched';
  const hours = triage.expectedBy ? hoursUntil(triage.expectedBy) : 0;
  const sr = (state: StepState) => (state === 'done' ? t('stepDone') : state === 'current' ? t('stepCurrent') : t('stepPending'));
  const reviewState: StepState = reviewed ? 'done' : 'current';
  const suggestState: StepState = triage.suggestionsReady ? 'done' : reviewed ? 'current' : 'pending';

  return (
    <div className="flex flex-col gap-6 pt-2">
      <div className="flex items-center gap-4">
        <span aria-hidden="true" className="flex h-24 w-24 shrink-0 items-center justify-center rounded-full bg-tint-teal">
          <svg width="56" height="56" viewBox="0 0 56 56" fill="none">
            <circle cx="28" cy="20" r="9" className="fill-tint-sand stroke-accent/40" strokeWidth="2" />
            <path d="M14 40c2-8 8-11 14-11s12 3 14 11z" className="fill-primary/40" />
            <rect x="12" y="44" width="32" height="4" rx="2" className="fill-primary/70" />
          </svg>
        </span>
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-bold">{t('title')}</h1>
          <p className="leading-relaxed text-muted">{t('subtitle')}</p>
        </div>
      </div>

      <ol aria-label={t('timelineLabel')} className="flex flex-col gap-5 rounded-card bg-surface p-5 shadow-[0_1px_0_rgb(var(--line))]">
        <Step
          state={triage.submittedAt ? 'done' : 'current'}
          srLabel={sr(triage.submittedAt ? 'done' : 'current')}
          title={t('sent')}
          detail={
            triage.submittedAt ? (
              <time dateTime={triage.submittedAt}>
                {t('sentAt', { day: formatRelativeDays(triage.submittedAt, locale, new Date(), me.timeZone), time: formatTime(triage.submittedAt, locale, me.timeZone) })}
              </time>
            ) : null
          }
        />
        <Step
          state={reviewState}
          srLabel={sr(reviewState)}
          title={reviewed ? t('reviewed') : t('reviewing')}
          detail={reviewed ? null : hours > 0 ? t('expected', { hours }) : t('expectedSoon')}
        />
        <Step
          state={suggestState}
          srLabel={sr(suggestState)}
          title={t('suggestions')}
          detail={
            triage.suggestionsReady ? (
              <Link href="/matches" className="inline-flex min-h-touch items-center font-semibold text-primary underline underline-offset-4">
                {t('viewMatches')}
              </Link>
            ) : (
              t('notify')
            )
          }
        />
      </ol>

      {triage.suggestionsReady ? null : <MatchReadyWatcher />}

      <WaitingActivities />

      <p className="rounded-card bg-surface p-5 leading-relaxed shadow-[0_1px_0_rgb(var(--line))]">
        {t.rich('helpHint', {
          help: (chunks) => (
            <Link href="/crisis-resources" className="font-bold text-help underline-offset-4 hover:underline">
              {chunks}
            </Link>
          ),
        })}
      </p>
    </div>
  );
}
