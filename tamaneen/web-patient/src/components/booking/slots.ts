import type { Slot } from '@/lib/api/types';

export interface SlotDay {
  /** YYYY-MM-DD in the patient's zone. */
  key: string;
  slots: Slot[];
}

const dayKey = (iso: string, timeZone: string) =>
  new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(iso));

/** Slots grouped by local day, both in chronological order. */
export function groupSlotsByDay(slots: readonly Slot[], timeZone: string): SlotDay[] {
  const days = new Map<string, Slot[]>();
  for (const slot of [...slots].sort((a, b) => Date.parse(a.startsAt) - Date.parse(b.startsAt))) {
    const key = dayKey(slot.startsAt, timeZone);
    const list = days.get(key);
    if (list) list.push(slot);
    else days.set(key, [slot]);
  }
  return [...days.entries()].map(([key, list]) => ({ key, slots: list }));
}

/** Seconds left until `expiresAt`, never negative. */
export function secondsLeft(expiresAt: string, now = Date.now()): number {
  return Math.max(0, Math.ceil((Date.parse(expiresAt) - now) / 1000));
}

/** "4:32" (ASCII digits in both languages, as in the design). */
export function formatCountdown(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}
