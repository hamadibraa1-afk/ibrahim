'use client';

import { useTranslations } from 'next-intl';
import { useId, useState, type FormEvent } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { CheckinRequest, CheckinResponse, CheckinStatus, HomeworkDetail } from '@/lib/api/types';
import { Avatar } from '@/components/ui/Avatar';
import { Button } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { TextArea } from '@/components/ui/TextField';
import { ScoreSlider } from '@/components/mood/ScoreSlider';
import { problemMessage } from '@/components/settings/problem';
import { SegmentedControl } from '@/components/settings/SegmentedControl';

const STATUSES: CheckinStatus[] = ['done', 'partial', 'skipped'];

/**
 * P10 check-in: how today went, how the patient felt afterwards (not asked on a skipped day), and an
 * optional note for the therapist, which goes through the crisis gate. Prefilled from today's check-in.
 */
export function HomeworkCheckinForm({ homework }: { homework: HomeworkDetail }) {
  const t = useTranslations('homework');
  const te = useTranslations('errors');
  const router = useRouter();
  const errorId = useId();
  const today = homework.todayCheckin;

  const [status, setStatus] = useState<CheckinStatus | null>(today?.status ?? null);
  const [feeling, setFeeling] = useState(today?.feeling ?? 5);
  const [note, setNote] = useState(today?.note ?? '');
  const [feedback, setFeedback] = useState(homework.latestFeedback);
  const [statusError, setStatusError] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaved(false);
    setError(null);
    if (!status) {
      setStatusError(true);
      return;
    }
    const body: CheckinRequest = { status, feeling: status === 'skipped' ? null : feeling };
    if (note.trim()) body.note = note.trim();
    setBusy(true);
    try {
      const result = await api<CheckinResponse>(`homework/${encodeURIComponent(homework.id)}/checkins`, { method: 'POST', body });
      if (result.crisis) {
        router.replace('/crisis');
        return;
      }
      setFeedback(result.homework.latestFeedback);
      setSaved(true);
    } catch (e) {
      setError(problemMessage(e, te('network')));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <SegmentedControl
          label={t('todayQuestion')}
          options={STATUSES.map((s) => ({ value: s, label: t(`status.${s}`) }))}
          value={status}
          invalid={statusError}
          describedBy={statusError ? errorId : undefined}
          onChange={(next) => {
            setStatus(next);
            setStatusError(false);
            setSaved(false);
          }}
        />
        {statusError ? (
          <p id={errorId} role="alert" className="text-sm font-semibold text-help">
            {t('statusRequired')}
          </p>
        ) : null}
      </div>

      {status !== 'skipped' ? (
        <ScoreSlider
          variant="scale"
          label={t('feelingLabel')}
          value={feeling}
          onChange={(v) => {
            setFeeling(v);
            setSaved(false);
          }}
          valueText={t('feelingValue', { value: feeling })}
        />
      ) : null}

      <TextArea
        label={t('noteLabel')}
        optional={t('optional')}
        value={note}
        rows={3}
        maxLength={1000}
        onChange={(e) => {
          setNote(e.target.value);
          setSaved(false);
        }}
      />

      {feedback ? (
        <figure aria-labelledby={`${errorId}-feedback`} className="flex items-start gap-2.5">
          <figcaption id={`${errorId}-feedback`} className="sr-only">{t('feedbackLabel')}</figcaption>
          <Avatar initials={feedback.initials} size={40} />
          <blockquote className="flex-1 rounded-[18px] border border-line bg-surface px-4 py-3 text-sm leading-relaxed">{feedback.text}</blockquote>
        </figure>
      ) : null}

      {error ? <Notice tone="danger">{error}</Notice> : null}
      {saved ? <Notice tone="success">{t('saved')}</Notice> : null}

      <Button type="submit" full busy={busy}>
        {t('save')}
      </Button>
    </form>
  );
}
