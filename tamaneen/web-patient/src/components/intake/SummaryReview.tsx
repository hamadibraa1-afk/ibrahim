'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useRef, useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { IntakeSummary, SummarySection, TriageState } from '@/lib/api/types';
import { apiMessage, isCrisis, isNetworkError } from '@/components/pwa/errors';
import { Button } from '@/components/ui/Button';
import { Tag } from '@/components/ui/Chip';
import { Notice } from '@/components/ui/Notice';
import { TextArea } from '@/components/ui/TextField';

const MAIN_SECTIONS: SummarySection[] = ['concern', 'goals', 'supports', 'priorCare'];
const MAX_SECTION_LENGTH = 4000;

/**
 * P05 Summary review: the patient checks and corrects what the assistant understood, section by
 * section, then sends it to the care team. Edits go through the crisis gate.
 */
export function SummaryReview({ initial, reviewHours }: { initial: IntakeSummary; reviewHours: number }) {
  const t = useTranslations('summary');
  const tErrors = useTranslations('errors');
  const router = useRouter();
  const [summary, setSummary] = useState(initial);
  const [editing, setEditing] = useState<SummarySection | null>(initial.generatedByAi ? null : 'concern');
  const [draft, setDraft] = useState(initial.generatedByAi ? '' : initial.sections.concern.text);
  const [busy, setBusy] = useState<'save' | 'submit' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const openers = useRef<Partial<Record<SummarySection, HTMLButtonElement | null>>>({});
  const returnFocus = useRef<SummarySection | null>(null);

  useEffect(() => {
    if (editing === null && returnFocus.current) {
      openers.current[returnFocus.current]?.focus();
      returnFocus.current = null;
    }
  }, [editing]);

  const failure = (e: unknown) => apiMessage(e) ?? (isNetworkError(e) ? tErrors('network') : tErrors('generic'));

  function open(section: SummarySection) {
    setEditing(section);
    setDraft(summary.sections[section].text);
    setSaved(false);
    setError(null);
  }

  function close(section: SummarySection) {
    returnFocus.current = section;
    setEditing(null);
  }

  async function save(section: SummarySection) {
    setBusy('save');
    setError(null);
    try {
      const response = await api<IntakeSummary>(`intake/${summary.intakeId}/summary`, { method: 'PUT', body: { sections: { [section]: draft.trim() } } });
      if (isCrisis(response)) {
        router.replace('/crisis');
        return;
      }
      setSummary(response);
      setSaved(true);
      close(section);
    } catch (e) {
      setError(failure(e));
    } finally {
      setBusy(null);
    }
  }

  async function submit() {
    if (editing) return;
    setBusy('submit');
    setError(null);
    try {
      const response = await api<TriageState>(`intake/${summary.intakeId}/submit`, { method: 'POST' });
      if (isCrisis(response)) {
        router.replace('/crisis');
        return;
      }
      router.push('/waiting');
    } catch (e) {
      setError(failure(e));
      setBusy(null);
    }
  }

  function renderSection(section: SummarySection) {
    const title = t(`sections.${section}`);
    const { text, editedByPatient } = summary.sections[section];
    const isEditing = editing === section;
    const writeMode = text.trim() === '';
    return (
      <li key={section} className="flex flex-col gap-3 border-b border-line p-5 last:border-b-0" data-testid={`section-${section}`}>
        <div className="flex items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <h2 id={`summary-${section}`} className="text-base font-bold">
              {title}
            </h2>
            {editedByPatient ? <Tag tone="sand">{t('editedTag')}</Tag> : null}
          </div>
          {!isEditing ? (
            <button
              ref={(el) => {
                openers.current[section] = el;
              }}
              type="button"
              aria-label={writeMode ? t('writeLabel', { section: title }) : t('editLabel', { section: title })}
              onClick={() => open(section)}
              disabled={editing !== null || busy !== null}
              className="min-h-touch shrink-0 px-2 text-sm font-semibold text-primary disabled:opacity-50"
            >
              {writeMode ? t('write') : t('edit')}
            </button>
          ) : null}
        </div>
        {isEditing ? (
          <div className="flex flex-col gap-3">
            <TextArea
              label={<span className="sr-only">{title}</span>}
              value={draft}
              rows={4}
              maxLength={MAX_SECTION_LENGTH}
              onChange={(e) => setDraft(e.target.value)}
              dir="auto"
              data-testid={`edit-${section}`}
            />
            <div className="flex gap-2">
              <Button compact busy={busy === 'save'} onClick={() => void save(section)}>
                {t('save')}
              </Button>
              <Button compact variant="secondary" disabled={busy === 'save'} onClick={() => close(section)}>
                {t('cancel')}
              </Button>
            </div>
          </div>
        ) : (
          <p dir="auto" className={`whitespace-pre-line leading-relaxed ${writeMode ? 'text-muted' : ''}`}>
            {writeMode ? t('empty') : text}
          </p>
        )}
      </li>
    );
  }

  const extra = summary.sections.extra;
  const showExtra = editing === 'extra' || extra.text.trim() !== '';

  return (
    <div className="flex flex-col gap-5 pt-2">
      <div className="flex flex-col gap-2">
        <h1 className="text-3xl font-bold leading-snug">{t('title')}</h1>
        <p className="text-base leading-relaxed text-muted">{t('subtitle')}</p>
      </div>

      {!summary.generatedByAi ? (
        <Notice tone="warning">
          <span className="block font-bold">{t('noAiTitle')}</span>
          {t('noAiBody')}
        </Notice>
      ) : null}

      <ul className="flex flex-col rounded-card border-[1.5px] border-line bg-surface">
        {MAIN_SECTIONS.map(renderSection)}
        {showExtra ? renderSection('extra') : null}
      </ul>

      {!showExtra ? (
        <button
          ref={(el) => {
            openers.current.extra = el;
          }}
          type="button"
          onClick={() => open('extra')}
          disabled={editing !== null || busy !== null}
          className="flex min-h-cta items-center justify-center rounded-card border-[1.5px] border-dashed border-muted/50 bg-surface px-4 font-semibold text-primary disabled:opacity-50"
        >
          {t('addExtra')}
        </button>
      ) : null}

      <div className="flex items-start gap-3 rounded-card bg-tint-teal p-4 text-primary-strong">
        <svg aria-hidden="true" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="mt-0.5 shrink-0">
          <circle cx="12" cy="12" r="9" />
          <path d="M12 7v5l3 2" />
        </svg>
        <p className="text-sm leading-relaxed">{t('reviewNote', { hours: reviewHours })}</p>
      </div>

      <div aria-live="polite" className="empty:hidden">
        {saved ? <p className="text-sm font-semibold text-primary">{t('saved')}</p> : null}
      </div>
      {error ? (
        <p role="alert" className="text-sm font-semibold text-help">
          {error}
        </p>
      ) : null}

      <div className="flex flex-col gap-2">
        <Button full busy={busy === 'submit'} disabled={editing !== null || busy === 'save'} aria-describedby={editing ? 'summary-save-first' : undefined} onClick={() => void submit()}>
          {t('submit')}
        </Button>
        {editing ? (
          <p id="summary-save-first" className="text-center text-sm text-muted">
            {t('saveFirst')}
          </p>
        ) : null}
      </div>
    </div>
  );
}
