import type { SessionState } from '@/lib/api/types';

/**
 * How far the device clock is from the server's (EC-09): countdowns add this to Date.now(), so a wrong phone
 * clock never opens or hides the session early. `receivedAt` is when the state arrived on the device.
 */
export function serverOffsetMs(serverNow: string, receivedAt: number): number {
  const server = Date.parse(serverNow);
  return Number.isFinite(server) ? server - receivedAt : 0;
}

/** "06:42"; over an hour "1:06:42". Negative values count as zero. */
export function mmss(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (n: number) => String(n).padStart(2, '0');
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
}

/**
 * What the lobby shows (P41, P54):
 * - `before`: counting down; the join button is disabled until the window opens.
 * - `open`: the window is open and the therapist is on time (or in the room).
 * - `late`: the start has passed and the therapist has not joined (P54), or they sent a "running late" notice.
 * - `lateOptions`: still late after `lateOptionsAfterMinutes`: the free reschedule and another therapist are offered.
 * - `closed`: the session time is over, or it is no longer confirmed.
 */
export type LobbyPhase = 'before' | 'open' | 'late' | 'lateOptions' | 'closed';

export function lobbyPhase(state: Pick<SessionState, 'status' | 'startsAt' | 'opensAt' | 'closesAt' | 'peer' | 'therapistLate' | 'lateOptionsAfterMinutes'>, now: number, therapistLateMinutes: number | null = null): LobbyPhase {
  if (state.status !== 'confirmed') return 'closed';
  const opens = Date.parse(state.opensAt);
  const closes = Date.parse(state.closesAt);
  const starts = Date.parse(state.startsAt);
  if (now > closes) return 'closed';
  const therapistIn = state.peer.present || state.peer.joinedAt !== null;
  const late = !therapistIn && (now > starts || (therapistLateMinutes !== null && now >= opens));
  if (late || (state.therapistLate && !therapistIn)) {
    return now - starts >= state.lateOptionsAfterMinutes * 60_000 ? 'lateOptions' : 'late';
  }
  return now >= opens ? 'open' : 'before';
}

/** The join button works in every phase but `before` and `closed`. */
export function canJoin(phase: LobbyPhase): boolean {
  return phase === 'open' || phase === 'late' || phase === 'lateOptions';
}
