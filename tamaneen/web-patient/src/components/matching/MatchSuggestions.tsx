'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useId, useRef, useState } from 'react';
import { Link } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { DismissReason, DismissRequest, Lang, MatchList, MatchSuggestion } from '@/lib/api/types';
import { formatNumber } from '@/lib/format';
import { Avatar } from '@/components/ui/Avatar';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Tag } from '@/components/ui/Chip';
import { EmptyState } from '@/components/ui/EmptyState';
import { Notice } from '@/components/ui/Notice';
import { PageHeading } from '@/components/ui/PageHeading';
import { Dialog } from '@/components/booking/Dialog';
import { ProblemNotice } from '@/components/booking/ProblemNotice';
import { toShownProblem, type ShownProblem } from '@/components/booking/problem';
import { asLocale, genderKey, specialtyLabels, type Translator } from './labels';
import { reasonSentence } from './reasons';

/** How many suggestions get the full card; the rest are compact rows, as in the design. */
const FULL_CARDS = 2;

/** "three therapists" is written out in the design; other counts use the number. */
const titleKey = (count: number) => (count === 1 ? 'titleOne' : count === 2 ? 'titleTwo' : count === 3 ? 'titleThree' : 'titleMany');

const REASONS: readonly DismissReason[] = ['specialty', 'gender', 'language', 'schedule', 'other'];

type Feedback = { kind: 'dismissed'; text: string } | { kind: 'error'; problem: ShownProblem };

/**
 * P07 ready state: the reviewed suggestions. "Not a good fit" opens a confirmation sheet with an optional
 * reason (PB-36), then removes the suggestion with no penalty.
 */
export function MatchSuggestions({ initial, patientLanguage, timeZone }: { initial: MatchSuggestion[]; patientLanguage: Lang; timeZone: string }) {
  const t = useTranslations('matching');
  const ts: Translator = useTranslations('specialties');
  const tErrors = useTranslations('errors');
  const locale = asLocale(useLocale());
  const [items, setItems] = useState(() => [...initial].sort((a, b) => a.rank - b.rank));
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<MatchSuggestion | null>(null);
  const [reason, setReason] = useState<DismissReason | null>(null);
  const reasonsId = useId();
  const feedbackRef = useRef<HTMLDivElement>(null);
  const [now] = useState(() => new Date());

  // The dismissed card (and its focused button) is gone; keep keyboard and screen-reader users in place.
  useEffect(() => {
    if (feedback) feedbackRef.current?.focus();
  }, [feedback]);

  function ask(suggestion: MatchSuggestion) {
    setReason(null);
    setConfirming(suggestion);
  }

  async function dismiss(suggestion: MatchSuggestion, chosen: DismissReason | null) {
    const { therapist } = suggestion;
    const previous = items;
    setConfirming(null);
    setBusyId(therapist.id);
    setItems(previous.filter((s) => s.therapist.id !== therapist.id));
    setFeedback({ kind: 'dismissed', text: t(genderKey('dismissed', therapist.gender), { name: therapist.displayName }) });
    try {
      const body: DismissRequest = chosen ? { reason: chosen } : {};
      const next = await api<MatchList>(`matches/${encodeURIComponent(therapist.id)}/dismiss`, { method: 'POST', body });
      setItems([...next.items].sort((a, b) => a.rank - b.rank));
    } catch (error) {
      setItems(previous);
      setFeedback({ kind: 'error', problem: toShownProblem(error, tErrors('generic')) });
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="flex flex-col gap-5 pt-4">
      <PageHeading title={t(titleKey(items.length), { n: formatNumber(items.length, locale) })} subtitle={t('subtitle')} />

      {feedback ? (
        <div ref={feedbackRef} tabIndex={-1} className="outline-none">
          {feedback.kind === 'dismissed' ? <Notice tone="success">{feedback.text}</Notice> : <ProblemNotice problem={feedback.problem} />}
        </div>
      ) : null}

      {items.length === 0 ? (
        <EmptyState title={t('emptyTitle')} body={t('emptyBody')} />
      ) : (
        <ul aria-label={t('listLabel')} className="flex flex-col gap-4">
          {items.map((suggestion, index) => {
            const { therapist } = suggestion;
            const labels = specialtyLabels(ts, therapist.specialties);
            if (index >= FULL_CARDS) {
              return (
                <li key={therapist.id}>
                  <Card className="flex items-center gap-3 border border-line">
                    <Avatar initials={therapist.initials} size={48} />
                    <div className="min-w-0 flex-1">
                      <h2 className="text-base font-bold">{therapist.displayName}</h2>
                      <p className="text-sm text-muted">{labels.join(' · ')}</p>
                    </div>
                    <Link
                      href={`/therapists/${encodeURIComponent(therapist.id)}`}
                      aria-label={t('viewLabel', { name: therapist.displayName })}
                      className="inline-flex min-h-touch items-center px-2 text-sm font-semibold text-primary underline underline-offset-4"
                    >
                      {t('view')}
                    </Link>
                  </Card>
                </li>
              );
            }

            const subtitle = [therapist.title, therapist.licenseVerified ? t(genderKey('licensed', therapist.gender)) : null].filter(Boolean).join(' · ');
            const sentence = reasonSentence({ reasons: suggestion.reasons, gender: therapist.gender, patientLanguage, locale, timeZone, now }, t, ts);
            return (
              <li key={therapist.id}>
                <Card className={`flex flex-col gap-3 ${suggestion.best ? 'border-[1.5px] border-primary' : 'border border-line'}`}>
                  <div className="flex items-start gap-3">
                    <Avatar initials={therapist.initials} size={56} />
                    <div className="min-w-0 flex-1">
                      <h2 className="text-lg font-bold">{therapist.displayName}</h2>
                      <p className="text-sm text-muted">{subtitle}</p>
                    </div>
                    {suggestion.best ? <Tag>{t('best')}</Tag> : null}
                  </div>
                  {labels.length > 0 ? (
                    <ul className="flex flex-wrap gap-2">
                      {labels.map((label) => (
                        <li key={label}>
                          <Tag tone="muted">{label}</Tag>
                        </li>
                      ))}
                    </ul>
                  ) : null}
                  {sentence ? (
                    <p className="rounded-[16px] bg-tint-sand px-4 py-3 text-sm leading-relaxed">
                      <strong className="me-1">{t(genderKey('reasons.label', therapist.gender))}</strong>
                      <span>{sentence}</span>
                    </p>
                  ) : null}
                  <div className="flex gap-3">
                    <ButtonLink href={`/therapists/${encodeURIComponent(therapist.id)}`} variant={suggestion.best ? 'primary' : 'secondary'} className="flex-1">
                      {t('viewProfile')}
                    </ButtonLink>
                    <Button
                      variant="secondary"
                      onClick={() => ask(suggestion)}
                      disabled={busyId !== null}
                      aria-label={t('dismissLabel', { name: therapist.displayName })}
                      className="text-ink"
                    >
                      {t('dismiss')}
                    </Button>
                  </div>
                </Card>
              </li>
            );
          })}
        </ul>
      )}

      <Dialog
        open={confirming !== null}
        onClose={() => setConfirming(null)}
        title={confirming ? t('dismissTitle', { name: confirming.therapist.displayName }) : ''}
        description={t('dismissBody')}
      >
        <fieldset className="flex flex-col gap-1">
          <legend className="mb-1 text-sm font-semibold">{t('dismissReasonLegend')}</legend>
          {REASONS.map((r) => (
            <label key={r} className="flex min-h-touch cursor-pointer items-center gap-3 text-base">
              <input type="radio" name={`${reasonsId}-reason`} value={r} checked={reason === r} onChange={() => setReason(r)} className="h-5 w-5 accent-[rgb(var(--primary))]" />
              {t(`dismissReasons.${r}`)}
            </label>
          ))}
        </fieldset>
        <div className="flex flex-col gap-2">
          <Button full onClick={() => confirming && void dismiss(confirming, reason)}>
            {t('dismissConfirm')}
          </Button>
          <Button full variant="secondary" onClick={() => setConfirming(null)}>
            {t('dismissCancel')}
          </Button>
        </div>
      </Dialog>

      <Link href="/therapists" className="mt-4 self-center py-3 text-base font-semibold text-primary underline underline-offset-4">
        {t('browseAll')}
      </Link>
    </div>
  );
}
