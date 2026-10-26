/**
 * Wall-clock helpers in the therapist's time zone (the API sends it, EC-09), independent of the browser's zone.
 * Dates are "yyyy-MM-dd", times "HH:mm".
 */

export const DEFAULT_TIME_ZONE = 'Asia/Riyadh';

export interface ZonedParts {
  date: string;
  time: string;
  hour: number;
  minute: number;
  /** 0 Sunday … 6 Saturday. */
  day: number;
}

const formatters = new Map<string, Intl.DateTimeFormat>();

function formatter(timeZone: string): Intl.DateTimeFormat {
  let f = formatters.get(timeZone);
  if (!f) {
    f = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
    formatters.set(timeZone, f);
  }
  return f;
}

export function zonedParts(value: string | Date, timeZone: string): ZonedParts {
  const parts = Object.fromEntries(formatter(timeZone).formatToParts(typeof value === 'string' ? new Date(value) : value).map((p) => [p.type, p.value]));
  const date = `${parts['year']}-${parts['month']}-${parts['day']}`;
  const hour = Number(parts['hour']) % 24;
  const minute = Number(parts['minute']);
  return { date, time: `${pad(hour)}:${pad(minute)}`, hour, minute, day: dayOfWeek(date) };
}

/** The UTC instant (ISO) of a wall-clock date and time in the zone. */
export function zonedToUtc(date: string, time: string, timeZone: string): string {
  const [y, m, d] = date.split('-').map(Number);
  const [h, mi] = time.split(':').map(Number);
  const wall = Date.UTC(y, m - 1, d, h, mi);
  let guess = wall;
  for (let i = 0; i < 2; i++) {
    const p = zonedParts(new Date(guess), timeZone);
    const [py, pm, pd] = p.date.split('-').map(Number);
    const seen = Date.UTC(py, pm - 1, pd, p.hour, p.minute);
    guess += wall - seen;
  }
  return new Date(guess).toISOString();
}

export function addDays(date: string, days: number): string {
  const [y, m, d] = date.split('-').map(Number);
  const t = new Date(Date.UTC(y, m - 1, d + days));
  return `${t.getUTCFullYear()}-${pad(t.getUTCMonth() + 1)}-${pad(t.getUTCDate())}`;
}

export function dayOfWeek(date: string): number {
  const [y, m, d] = date.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d)).getUTCDay();
}

/** The Sunday that starts the week of the date (the working week runs Sunday to Thursday). */
export function weekStart(date: string): string {
  return addDays(date, -dayOfWeek(date));
}

export function today(timeZone: string, now: Date = new Date()): string {
  return zonedParts(now, timeZone).date;
}

/** "HH:mm" to minutes after midnight. */
export function minutesOf(time: string): number {
  const [h, m] = time.split(':').map(Number);
  return h * 60 + m;
}

function pad(n: number): string {
  return String(n).padStart(2, '0');
}
