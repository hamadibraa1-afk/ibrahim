'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { useReducedMotion } from '@/components/pwa/hooks';
import { Button } from '@/components/ui/Button';

export type BreathPhase = 'inhale' | 'hold' | 'exhale';

/** 4 s in, 4 s hold, 6 s out, for three minutes. */
export const BREATH_PATTERN: readonly { phase: BreathPhase; seconds: number }[] = [
  { phase: 'inhale', seconds: 4 },
  { phase: 'hold', seconds: 4 },
  { phase: 'exhale', seconds: 6 },
];
export const BREATH_CYCLE_SECONDS = BREATH_PATTERN.reduce((sum, p) => sum + p.seconds, 0);
export const BREATHING_TOTAL_SECONDS = 180;

/** The phase at a given second of the exercise, and how many seconds of it are left. */
export function breathPhaseAt(elapsed: number): { phase: BreathPhase; secondsLeft: number } {
  let position = ((elapsed % BREATH_CYCLE_SECONDS) + BREATH_CYCLE_SECONDS) % BREATH_CYCLE_SECONDS;
  for (const step of BREATH_PATTERN) {
    if (position < step.seconds) return { phase: step.phase, secondsLeft: step.seconds - position };
    position -= step.seconds;
  }
  return { phase: 'inhale', secondsLeft: BREATH_PATTERN[0]!.seconds };
}

function clock(seconds: number): string {
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}:${String(s).padStart(2, '0')}`;
}

/**
 * An in-page breathing pacer. The instruction text changes with each phase and is announced politely, so
 * the exercise works by ear. A circle grows and shrinks with the breath, except when the person asked for
 * reduced motion: then it is text only.
 */
export function BreathingPacer() {
  const t = useTranslations('waitingTriage.breathing');
  const reduced = useReducedMotion();
  const [running, setRunning] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const [done, setDone] = useState(false);

  useEffect(() => {
    if (!running) return;
    const id = window.setInterval(() => {
      setElapsed((e) => Math.min(e + 1, BREATHING_TOTAL_SECONDS));
    }, 1000);
    return () => window.clearInterval(id);
  }, [running]);

  useEffect(() => {
    if (running && elapsed >= BREATHING_TOTAL_SECONDS) {
      setRunning(false);
      setDone(true);
    }
  }, [running, elapsed]);

  function start() {
    setElapsed(0);
    setDone(false);
    setRunning(true);
  }

  function stop() {
    setRunning(false);
    setElapsed(0);
  }

  const { phase, secondsLeft } = breathPhaseAt(elapsed);
  const instruction = done ? t('done') : running ? t(phase) : t('ready');
  const expanded = running && phase !== 'exhale';
  const duration = BREATH_PATTERN.find((p) => p.phase === phase)!.seconds;

  return (
    <section aria-labelledby="breathing-title" className="flex flex-col items-center gap-4 rounded-card bg-tint-teal p-5 text-center" data-testid="breathing">
      <h3 id="breathing-title" className="sr-only">
        {t('label')}
      </h3>
      <p className="text-sm text-primary-strong">{t('intro')}</p>

      {!reduced ? (
        <div aria-hidden="true" className="flex h-44 w-44 items-center justify-center" data-testid="breathing-circle">
          <div
            className="h-44 w-44 rounded-full bg-primary/25 ring-4 ring-primary/40"
            style={{
              transform: `scale(${expanded ? 1 : 0.55})`,
              transition: running ? `transform ${duration}s ease-in-out` : 'transform 0.3s ease-out',
            }}
          />
        </div>
      ) : null}

      <p aria-live="polite" className="min-h-[2rem] text-2xl font-bold text-primary-strong" data-testid="breathing-instruction">
        {instruction}
      </p>
      {running ? (
        <p className="text-sm tabular-nums text-primary-strong">
          <span aria-hidden="true" className="me-2 inline-block min-w-[1ch] font-bold">
            {secondsLeft}
          </span>
          {t('remaining', { time: clock(BREATHING_TOTAL_SECONDS - elapsed) })}
        </p>
      ) : null}

      {running ? (
        <Button variant="secondary" onClick={stop}>
          {t('stop')}
        </Button>
      ) : (
        <Button onClick={start}>{done ? t('again') : t('start')}</Button>
      )}
    </section>
  );
}
