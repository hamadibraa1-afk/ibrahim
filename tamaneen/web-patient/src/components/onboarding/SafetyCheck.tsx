'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useRef, useState } from 'react';
import { Link, useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { GateOutcome, ScreeningForm, ScreeningSubmit } from '@/lib/api/types';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';
import { OptionButton } from '@/components/ui/OptionButton';
import { ProgressBar } from '@/components/ui/ProgressBar';
import { Spinner } from '@/components/ui/Spinner';
import { problemText } from '@/components/auth/errorText';

/**
 * P23 Safety check (C-SSRS screener) for the direct-booking path. One question at a time, no skipping and no
 * going back. Every answer goes to the crisis gate as it is given (the answers so far): a «نعم» that triggers
 * the gate opens P06 at once, even before the last question. After the last answer, the directory.
 */
export function SafetyCheck({ form, emergencyNumber }: { form: ScreeningForm; emergencyNumber: string | null }) {
  const t = useTranslations('safetyCheck');
  const tu = useTranslations('ui');
  const te = useTranslations('errors');
  const router = useRouter();
  const questions = [...form.questions].sort((a, b) => a.index - b.index);
  const total = questions.length;
  const [index, setIndex] = useState(0);
  const [answers, setAnswers] = useState<(boolean | undefined)[]>(() => Array.from({ length: total }, () => undefined));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const firstRender = useRef(true);

  useEffect(() => {
    if (firstRender.current) {
      firstRender.current = false;
      return;
    }
    headingRef.current?.focus();
  }, [index]);

  /** Sends the answers given so far (index 0..upTo) to the gate, then moves on. */
  async function submit(given: boolean[]) {
    setBusy(true);
    setError(null);
    try {
      const body: ScreeningSubmit = { answers: given };
      const outcome = await api<GateOutcome>('screening/cssrs', { method: 'POST', body });
      if (outcome.crisis) {
        router.replace('/crisis');
        return;
      }
      if (given.length >= total) {
        router.push('/therapists');
        return;
      }
      setIndex(given.length);
      setBusy(false);
    } catch (e) {
      setError(problemText(e, te('network')));
      setBusy(false);
    }
  }

  function answer(value: boolean) {
    if (busy || answers[index] !== undefined) return;
    const next = answers.map((a, i) => (i === index ? value : a));
    setAnswers(next);
    void submit(next.slice(0, index + 1) as boolean[]);
  }

  const question = questions[index];
  const current = answers[index];
  const progress = total === 0 ? 0 : ((index + (current === undefined ? 0 : 1)) / total) * 100;

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-col gap-2">
        <div className="flex items-center justify-between gap-3 text-sm font-semibold">
          <h1 className="text-primary-strong">{t('label')}</h1>
          <p className="text-muted" data-testid="question-count">
            {t('progress', { current: index + 1, total })}
          </p>
        </div>
        <ProgressBar value={progress} label={t('progressLabel')} />
      </div>

      <p className="text-base leading-relaxed text-muted">{t('intro')}</p>

      {question ? (
        <Card className="flex flex-col gap-4 border border-line">
          <p className="text-sm font-semibold text-primary">{t('eyebrow')}</p>
          <h2 ref={headingRef} tabIndex={-1} id="cssrs-question" className="text-xl font-bold leading-relaxed outline-none">
            {question.text}
          </h2>
          <div role="radiogroup" aria-labelledby="cssrs-question" className="grid grid-cols-2 gap-3">
            <OptionButton selected={current === true} onClick={() => answer(true)} disabled={busy || current !== undefined} className="min-h-[60px] justify-center text-center">
              {tu('yes')}
            </OptionButton>
            <OptionButton selected={current === false} onClick={() => answer(false)} disabled={busy || current !== undefined} className="min-h-[60px] justify-center text-center">
              {tu('no')}
            </OptionButton>
          </div>
        </Card>
      ) : null}

      {form.note ? <p className="text-sm leading-relaxed text-muted">{form.note}</p> : null}

      {busy ? <Spinner label={t('submitting')} /> : null}
      {error ? (
        <div className="flex flex-col gap-3">
          <Notice tone="danger">{error}</Notice>
          <Button variant="secondary" onClick={() => void submit(answers.slice(0, index + 1) as boolean[])}>
            {t('retry')}
          </Button>
        </div>
      ) : null}

      <div className="mt-6 rounded-[16px] bg-help/10 px-4 py-3 text-sm leading-relaxed text-help">
        {emergencyNumber ? (
          t.rich('helpCall', {
            number: emergencyNumber,
            b: (chunks) => (
              <a href={`tel:${emergencyNumber}`} className="font-bold underline underline-offset-4">
                <bdi dir="ltr">{chunks}</bdi>
              </a>
            ),
          })
        ) : (
          <>
            {t('helpPrefix')}{' '}
            <Link href="/crisis-resources" className="font-bold underline underline-offset-4">
              {t('helpResources')}
            </Link>
          </>
        )}
      </div>
    </div>
  );
}
