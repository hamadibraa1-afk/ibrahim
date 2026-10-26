'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useRef, useState } from 'react';
import { CheckboxRow } from '@/components/onboarding/CheckboxRow';
import { asLocale, genderKey } from '@/components/matching/labels';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';
import { TextArea } from '@/components/ui/TextField';
import { Link, useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { Appointment, AppointmentDetail, HomeworkItem, SessionFeedback, SessionFeedbackSaved, SessionRating } from '@/lib/api/types';
import { formatDateTime, formatNumber } from '@/lib/format';

const RATINGS: readonly SessionRating[] = ['not_helpful', 'okay', 'helpful'];
export const COMMENT_MAX = 500;

/** "Well done, you finished your first session": the heading follows the session number (`t` is session.summary). */
export function doneHeading(t: (key: string, values?: Record<string, string>) => string, sequenceNumber: number, locale: 'ar' | 'en'): string {
  const n = Math.max(1, Math.floor(sequenceNumber));
  return n <= 10 ? t(`done${n}`) : t('doneN', { num: formatNumber(n, locale) });
}

/**
 * P43 after the session: the next appointment (or booking it), the patient's homework, and the private rating that
 * reaches the supervisor only. Saving is once per session with an Idempotency-Key; an error keeps the choice; an
 * existing rating is shown read-only. The therapist's patient summary has no API yet, so that card is left out.
 */
export function SessionSummaryView({
  appointment,
  next,
  homework,
  feedback,
  timeZone,
}: {
  appointment: AppointmentDetail;
  next: Appointment | null;
  homework: HomeworkItem[];
  feedback: SessionFeedback | null;
  timeZone: string;
}) {
  const t = useTranslations('session.summary');
  const tHw = useTranslations('homework');
  const tUi = useTranslations('ui');
  const locale = asLocale(useLocale());
  const router = useRouter();
  const [rating, setRating] = useState<SessionRating | null>(null);
  const [comfortable, setComfortable] = useState(false);
  const [comment, setComment] = useState('');
  const [saving, setSaving] = useState(false);
  const [failed, setFailed] = useState(false);
  const key = useRef<string>(typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}`);
  const g = appointment.therapist.gender;
  const bookHref = `/therapists/${encodeURIComponent(appointment.therapist.id)}/book`;
  const canRate = appointment.status === 'confirmed' || appointment.status === 'completed';

  async function save() {
    const body = { rating: rating ?? undefined, comfortable: comfortable || undefined, comment: comment.trim() || undefined };
    if (!body.rating && !body.comfortable && !body.comment) {
      router.push('/home');
      return;
    }
    setSaving(true);
    setFailed(false);
    try {
      const result = await api<SessionFeedbackSaved>(`appointments/${encodeURIComponent(appointment.id)}/feedback`, { method: 'POST', body, idempotencyKey: key.current });
      router.replace(result.crisis ? '/crisis' : '/home');
    } catch {
      setFailed(true);
      setSaving(false);
    }
  }

  return (
    <div className="flex flex-col gap-5">
      <h1 className="text-2xl font-bold leading-snug">{doneHeading(t, appointment.sequenceNumber, locale)}</h1>

      <Card className="flex flex-col gap-2 border border-line" aria-labelledby="next-title">
        <h2 id="next-title" className="text-lg font-bold">
          {t('next')}
        </h2>
        {next ? (
          <div className="flex items-center justify-between gap-3">
            <p className="text-base">{formatDateTime(next.startsAt, locale, timeZone)}</p>
            <Link href={`/appointments/${encodeURIComponent(next.id)}`} className="font-semibold text-primary">
              {t('details')}
            </Link>
          </div>
        ) : (
          <>
            <p className="text-base text-muted">{t('nextNone')}</p>
            <ButtonLink href={bookHref} variant="secondary" full>
              {t('book')}
            </ButtonLink>
          </>
        )}
      </Card>

      {homework.length > 0 ? (
        <Card className="flex flex-col gap-2 border border-line" aria-labelledby="hw-title">
          <h2 id="hw-title" className="text-lg font-bold">
            {t('homework')}
          </h2>
          <ul className="flex flex-col gap-2">
            {homework.map((h) => (
              <li key={h.id} className="flex items-center justify-between gap-3">
                <span>
                  {h.title} · {tHw(`frequency.${h.frequency}`)}
                </span>
                <Link href={`/homework/${encodeURIComponent(h.id)}`} className="font-semibold text-primary" aria-label={`${t('view')}: ${h.title}`}>
                  {t('view')}
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      ) : null}

      {feedback ? (
        <Card className="flex flex-col gap-2 border border-line" data-testid="feedback-readonly">
          <h2 className="text-lg font-bold">{t('ratingTitle')}</h2>
          <p className="text-base">{[feedback.rating ? t(`ratings.${feedback.rating}`) : null, feedback.comfortable ? t('comfortableYes') : null].filter(Boolean).join(' · ')}</p>
          {feedback.comment ? <p className="whitespace-pre-line text-base text-muted">{feedback.comment}</p> : null}
          <p className="text-sm text-muted">{t('rated')}</p>
          <ButtonLink href="/home" full>
            {t('back')}
          </ButtonLink>
        </Card>
      ) : canRate ? (
        <Card className="flex flex-col gap-3 border border-line">
          <fieldset className="flex flex-col gap-3">
            <legend className="text-lg font-bold">{t('ratingTitle')}</legend>
            <p className="text-sm text-muted">{t('ratingPrivate')}</p>
            <div className="grid grid-cols-3 gap-2">
              {RATINGS.map((r) => (
                <label key={r} className={`flex min-h-[48px] cursor-pointer items-center justify-center rounded-[14px] border px-2 text-center text-sm font-semibold has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-primary ${rating === r ? 'border-primary bg-tint-teal text-primary-strong' : 'border-line'}`}>
                  <input type="radio" name="rating" value={r} checked={rating === r} onChange={() => setRating(r)} className="sr-only" />
                  {t(`ratings.${r}`)}
                </label>
              ))}
            </div>
          </fieldset>
          <CheckboxRow checked={comfortable} onChange={setComfortable} label={t(genderKey('comfortable', g))} />
          <TextArea label={t('comment')} optional={`(${tUi('optional')})`} value={comment} maxLength={COMMENT_MAX} rows={3} onChange={(e) => setComment(e.target.value)} />
          {failed ? <Notice tone="danger">{t('saveError')}</Notice> : null}
          <Button full busy={saving} onClick={() => void save()}>
            {t('save')}
          </Button>
        </Card>
      ) : (
        <ButtonLink href="/home" full>
          {t('back')}
        </ButtonLink>
      )}
    </div>
  );
}
