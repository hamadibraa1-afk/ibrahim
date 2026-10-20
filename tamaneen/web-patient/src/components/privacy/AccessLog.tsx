'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useId, useState } from 'react';
import { isLocale, routing } from '@/i18n/routing';
import type { AccessLogEntry } from '@/lib/api/types';
import { formatDayAndTime } from '@/components/settings/time';

const ACTIONS: Record<string, string> = {
  'patient.file_opened': 'fileOpened',
  'triage.reviewed': 'triageReviewed',
  'intake.summary_generated': 'summaryGenerated',
  'crisis.contact_revealed': 'contactRevealed',
  'crisis.acknowledged': 'crisisAcknowledged',
};

const PREVIEW = 3;

/** The translation key for an audit action code; codes this app doesn't know read as "viewed your file". */
export function actionKey(code: string): string {
  return ACTIONS[code] ?? 'unknown';
}

/** "Who opened my file" (P11): the latest entries, with the full log one tap away. */
export function AccessLog({ entries, nowIso, timeZone }: { entries: AccessLogEntry[]; nowIso: string; timeZone: string }) {
  const t = useTranslations('privacy');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  const listId = useId();
  const [expanded, setExpanded] = useState(false);
  const now = new Date(nowIso);
  const sorted = [...entries].sort((a, b) => Date.parse(b.atUtc) - Date.parse(a.atUtc));
  const shown = expanded ? sorted : sorted.slice(0, PREVIEW);

  return (
    <section aria-labelledby="privacy-access" className="flex flex-col gap-2">
      <div className="flex items-center justify-between gap-3">
        <h2 id="privacy-access" className="text-lg font-bold">
          {t('accessTitle')}
        </h2>
        {sorted.length > PREVIEW ? (
          <button
            type="button"
            aria-expanded={expanded}
            aria-controls={listId}
            onClick={() => setExpanded((v) => !v)}
            className="min-h-touch text-sm font-semibold text-primary underline underline-offset-4"
          >
            {expanded ? t('showLess') : t('fullLog')}
          </button>
        ) : null}
      </div>
      {sorted.length === 0 ? (
        <p className="rounded-card border border-line bg-surface px-4 py-4 text-sm text-muted">{t('accessEmpty')}</p>
      ) : (
        <ul id={listId} className="divide-y divide-line rounded-card border border-line bg-surface">
          {shown.map((entry, i) => (
            <li key={`${entry.atUtc}-${i}`} className="flex items-center justify-between gap-3 px-4 py-3">
              <span className="flex min-w-0 flex-col">
                <span className="font-bold">{t(`actors.${entry.actor}`)}</span>
                <span className="text-sm text-muted">{t(`actions.${actionKey(entry.action)}.${entry.actor === 'therapist' ? 'feminine' : 'masculine'}`)}</span>
              </span>
              <time dateTime={entry.atUtc} className="shrink-0 text-sm text-muted">
                {formatDayAndTime(entry.atUtc, locale, now, timeZone)}
              </time>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
