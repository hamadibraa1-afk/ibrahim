'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { api, ApiError } from '@/lib/api/client';
import type { ChallengeResponse } from '@/lib/api/types';
import { Button } from '@/components/ui/Button';
import { problemText } from './errorText';
import { formatSeconds, useCountdown } from './useCountdown';

interface ResendCountdownProps {
  challengeId: string;
  /** When the resend button unlocks (ms since epoch). */
  availableAt: number;
  /** A new code was sent (the challenge id may change), or the cooldown was extended by a 429. */
  onChange: (next: { challengeId: string; maskedContact?: string; availableAt: number }) => void;
}

/**
 * "Didn't get the code? You can request a new one in 0:42" with a live countdown, then a resend button.
 * The ticking time is a timer (not announced every second); a polite live region says when resend opens
 * and when a new code was sent.
 */
export function ResendCountdown({ challengeId, availableAt, onChange }: ResendCountdownProps) {
  const t = useTranslations('verify');
  const te = useTranslations('errors');
  const seconds = useCountdown(availableAt);
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function resend() {
    setBusy(true);
    setError(null);
    setSent(false);
    try {
      const next = await api<ChallengeResponse>(`auth/challenges/${encodeURIComponent(challengeId)}/resend`, { method: 'POST' });
      onChange({ challengeId: next.challengeId, maskedContact: next.maskedContact, availableAt: Date.now() + next.resendAfterSeconds * 1000 });
      setSent(true);
    } catch (e) {
      if (e instanceof ApiError && e.status === 429) {
        const wait = e.problem.retryAfterSeconds ?? 60;
        onChange({ challengeId, availableAt: Date.now() + wait * 1000 });
      } else {
        setError(problemText(e, te('network')));
      }
    } finally {
      setBusy(false);
    }
  }

  const waiting = seconds > 0;
  return (
    <div className="flex flex-col items-center gap-2 text-center">
      {waiting ? (
        <p className="text-sm text-muted">
          {t.rich('wait', {
            time: formatSeconds(seconds),
            b: (chunks) => (
              <b role="timer" data-testid="resend-timer" dir="ltr" className="font-bold text-ink">
                {chunks}
              </b>
            ),
          })}
        </p>
      ) : (
        <p className="text-sm text-muted">{t('ready')}</p>
      )}
      <p aria-live="polite" className="sr-only">
        {sent ? t('resent') : waiting ? null : t('ready')}
      </p>
      <Button variant="ghost" compact onClick={resend} disabled={waiting} busy={busy}>
        {t('resend')}
      </Button>
      {sent ? <p className="text-sm font-semibold text-primary">{t('resent')}</p> : null}
      {error ? (
        <p role="alert" className="text-sm font-semibold text-help">
          {error}
        </p>
      ) : null}
    </div>
  );
}
