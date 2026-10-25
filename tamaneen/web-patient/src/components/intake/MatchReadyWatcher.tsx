'use client';

import { useEffect } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { TriageState } from '@/lib/api/types';

export const MATCH_POLL_MS = 30_000;

/**
 * P36 «اعتماد الفرز: MatchReady ينقل إلى P07». The contract has no patient hub, so this polls GET /v1/triage
 * while the tab is visible (and once more when it becomes visible again), and moves to the matches as soon as
 * the suggestions are ready. Failed polls are ignored; the next one tries again. Renders nothing.
 */
export function MatchReadyWatcher({ intervalMs = MATCH_POLL_MS }: { intervalMs?: number }) {
  const router = useRouter();

  useEffect(() => {
    let stopped = false;
    let inFlight = false;

    async function check() {
      if (stopped || inFlight || document.visibilityState === 'hidden') return;
      inFlight = true;
      try {
        const triage = await api<TriageState>('triage');
        if (!stopped && triage.suggestionsReady) {
          stopped = true;
          router.replace('/matches');
        }
      } catch {
        // Offline or a hiccup: keep waiting.
      } finally {
        inFlight = false;
      }
    }

    const timer = window.setInterval(check, intervalMs);
    const onVisible = () => {
      if (document.visibilityState === 'visible') void check();
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      stopped = true;
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [intervalMs, router]);

  return null;
}
