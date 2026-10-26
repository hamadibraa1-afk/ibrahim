'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '@/lib/api/client';
import type { SessionState } from '@/lib/api/types';
import { serverOffsetMs } from './sessionClock';

/**
 * The lobby's view of the room (P41, P54): polls GET presence every heartbeat, keeps the server-clock offset and
 * ticks once a second. When a poll fails the last known state stays and `online` turns false, so the countdown
 * carries on from the last known time.
 */
export function useSessionState(appointmentId: string, initial: SessionState | null) {
  const [state, setState] = useState<SessionState | null>(initial);
  const [offset, setOffset] = useState(() => (initial ? serverOffsetMs(initial.serverNow, Date.now()) : 0));
  const [online, setOnline] = useState(true);
  const [now, setNow] = useState(() => Date.now());
  const active = useRef(true);

  const poll = useCallback(async () => {
    try {
      const next = await api<SessionState>(`appointments/${encodeURIComponent(appointmentId)}/presence`);
      if (!active.current) return;
      setState(next);
      setOffset(serverOffsetMs(next.serverNow, Date.now()));
      setOnline(true);
    } catch {
      if (active.current) setOnline(false);
    }
  }, [appointmentId]);

  useEffect(() => {
    active.current = true;
    if (!initial) void poll();
    const seconds = Math.max(5, state?.heartbeatSeconds ?? 15);
    const id = window.setInterval(() => void poll(), seconds * 1000);
    return () => {
      active.current = false;
      window.clearInterval(id);
    };
    // Only the interval length matters here; the state itself changes every poll.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [poll, state?.heartbeatSeconds]);

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    const offline = () => setOnline(false);
    const back = () => void poll();
    window.addEventListener('offline', offline);
    window.addEventListener('online', back);
    return () => {
      window.clearInterval(id);
      window.removeEventListener('offline', offline);
      window.removeEventListener('online', back);
    };
  }, [poll]);

  return { state, serverNow: now + offset, online, refresh: poll };
}
