import { useLocale, useTranslations } from 'next-intl';
import { useId } from 'react';
import { isLocale, routing } from '@/i18n/routing';
import type { MoodEntry } from '@/lib/api/types';
import { formatDate } from '@/lib/format';
import { Card } from '@/components/ui/Card';
import { dayKey } from '@/components/settings/time';

export interface MoodDay {
  key: string;
  date: Date;
  score: number | null;
  today: boolean;
}

/** The seven calendar days ending today (oldest first) with the latest score logged on each. */
export function buildWeek(entries: MoodEntry[], now: Date, timeZone: string): MoodDay[] {
  const latest = new Map<string, MoodEntry>();
  for (const entry of entries) {
    const key = dayKey(new Date(entry.at), timeZone);
    const seen = latest.get(key);
    if (!seen || Date.parse(entry.at) > Date.parse(seen.at)) latest.set(key, entry);
  }
  return Array.from({ length: 7 }, (_, i) => {
    const date = new Date(now.getTime() - (6 - i) * 86_400_000);
    const key = dayKey(date, timeZone);
    return { key, date, score: latest.get(key)?.score ?? null, today: i === 6 };
  });
}

/**
 * "Last 7 days" bars. The bars are decoration; screen readers get the same data as a table of day → score.
 */
export function MoodChart({ entries, now, timeZone }: { entries: MoodEntry[]; now: Date; timeZone: string }) {
  const t = useTranslations('checkin');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  const headingId = useId();
  const days = buildWeek(entries, now, timeZone);
  const empty = days.every((d) => d.score === null);

  return (
    <Card aria-labelledby={headingId} className="flex flex-col gap-4">
      <h2 id={headingId} className="text-sm font-semibold text-muted">
        {t('last7')}
      </h2>
      <div aria-hidden="true" data-testid="mood-bars" className="flex h-20 items-end gap-2">
        {days.map((day) => (
          <div
            key={day.key}
            style={{ height: day.score === null ? '6%' : `${Math.max(8, day.score * 10)}%` }}
            className={`flex-1 rounded-[8px] ${day.score === null ? 'border border-dashed border-line' : day.today ? 'bg-primary' : 'bg-primary/40'}`}
          />
        ))}
      </div>
      {empty ? <p className="text-sm text-muted">{t('chartEmpty')}</p> : null}
      <table className="sr-only">
        <caption>{t('last7')}</caption>
        <thead>
          <tr>
            <th scope="col">{t('chartDay')}</th>
            <th scope="col">{t('chartScore')}</th>
          </tr>
        </thead>
        <tbody>
          {days.map((day) => (
            <tr key={day.key}>
              <th scope="row">{formatDate(day.date.toISOString(), locale, timeZone)}</th>
              <td>{day.score === null ? t('chartNoEntry') : t('chartScoreValue', { score: day.score })}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Card>
  );
}
