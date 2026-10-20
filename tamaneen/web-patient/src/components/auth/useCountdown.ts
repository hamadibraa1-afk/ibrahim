'use client';

import { useEffect, useState } from 'react';

/** Whole seconds left until `deadline` (ms since epoch), updated every second, never below zero. */
export function useCountdown(deadline: number | null): number {
  const left = () => (deadline === null ? 0 : Math.max(0, Math.ceil((deadline - Date.now()) / 1000)));
  const [seconds, setSeconds] = useState(left);

  useEffect(() => {
    setSeconds(left());
    if (deadline === null) return;
    const timer = window.setInterval(() => {
      const next = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
      setSeconds(next);
      if (next === 0) window.clearInterval(timer);
    }, 1000);
    return () => window.clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deadline]);

  return seconds;
}

/** 42 → "0:42", 75 → "1:15" (Latin digits in both languages, like the design). */
export function formatSeconds(total: number): string {
  const m = Math.floor(total / 60);
  const s = total % 60;
  return `${m}:${String(s).padStart(2, '0')}`;
}
