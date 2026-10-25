'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useId, useState, type FormEvent } from 'react';
import { Link } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { TherapistSummary } from '@/lib/api/types';
import { formatNumber, formatRelativeDays } from '@/lib/format';
import { Avatar } from '@/components/ui/Avatar';
import { Chip } from '@/components/ui/Chip';
import { EmptyState } from '@/components/ui/EmptyState';
import { ProblemNotice } from '@/components/booking/ProblemNotice';
import { toShownProblem, type ShownProblem } from '@/components/booking/problem';
import { emptyFilters, filtersToQuery, hasFilters, therapistsPath, type DirectoryFilters } from './directoryQuery';
import { asLocale, countKey, specialtyLabels, type Translator } from './labels';

export const SEARCH_DEBOUNCE_MS = 350;

type ChipKey = 'anxiety' | 'depression' | 'arabic' | 'female' | 'evening';

const chips: { key: ChipKey; active: (f: DirectoryFilters) => boolean; toggle: (f: DirectoryFilters) => DirectoryFilters }[] = [
  { key: 'anxiety', active: (f) => f.specialty === 'anxiety', toggle: (f) => ({ ...f, specialty: f.specialty === 'anxiety' ? null : 'anxiety' }) },
  { key: 'depression', active: (f) => f.specialty === 'depression', toggle: (f) => ({ ...f, specialty: f.specialty === 'depression' ? null : 'depression' }) },
  { key: 'arabic', active: (f) => f.language === 'ar', toggle: (f) => ({ ...f, language: f.language === 'ar' ? null : 'ar' }) },
  { key: 'female', active: (f) => f.gender === 'female', toggle: (f) => ({ ...f, gender: f.gender === 'female' ? null : 'female' }) },
  { key: 'evening', active: (f) => f.evening, toggle: (f) => ({ ...f, evening: !f.evening }) },
];

/**
 * P24 directory. The first page arrives server-rendered for the URL's filters; after that every change
 * rewrites the query string (so back and refresh keep it) and fetches GET /v1/therapists from the browser.
 */
export function TherapistDirectory({ initialFilters, initialResults, timeZone }: { initialFilters: DirectoryFilters; initialResults: TherapistSummary[]; timeZone: string }) {
  const t = useTranslations('therapists');
  const tSpecialties: Translator = useTranslations('specialties');
  const tErrors = useTranslations('errors');
  const locale = asLocale(useLocale());
  const searchId = useId();

  const [filters, setFilters] = useState(initialFilters);
  const [text, setText] = useState(initialFilters.q);
  const [results, setResults] = useState(initialResults);
  const [resultsQuery, setResultsQuery] = useState(() => filtersToQuery(initialFilters));
  const [loading, setLoading] = useState(false);
  const [problem, setProblem] = useState<ShownProblem | null>(null);
  const [now] = useState(() => new Date());
  const query = filtersToQuery(filters);

  // Typing settles before it becomes a filter.
  useEffect(() => {
    const q = text.trim();
    if (q === filters.q) return;
    const id = setTimeout(() => setFilters((f) => ({ ...f, q })), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(id);
  }, [text, filters.q]);

  // The URL mirrors the filters; replaceState keeps one history entry for the directory.
  useEffect(() => {
    const search = query ? `?${query}` : '';
    if (window.location.search !== search) {
      window.history.replaceState(null, '', `${window.location.pathname}${search}`);
    }
  }, [query]);

  useEffect(() => {
    if (query === resultsQuery) return;
    const controller = new AbortController();
    setLoading(true);
    setProblem(null);
    api<TherapistSummary[]>(therapistsPath(filters), { signal: controller.signal })
      .then((list) => {
        setResults(list);
        setResultsQuery(query);
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setProblem(toShownProblem(error, tErrors('generic')));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [filters, query, resultsQuery, tErrors]);

  function submit(event: FormEvent) {
    event.preventDefault();
    setFilters((f) => ({ ...f, q: text.trim() }));
  }

  function clear() {
    setText('');
    setFilters(emptyFilters);
  }

  const showClear = hasFilters(filters) || text.trim() !== '';

  return (
    <div className="flex flex-col gap-4 pt-2">
      <h1 className="text-2xl font-bold leading-snug">{t('title')}</h1>

      <form role="search" onSubmit={submit}>
        <label htmlFor={searchId} className="sr-only">
          {t('searchLabel')}
        </label>
        <input
          id={searchId}
          type="search"
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder={t('searchPlaceholder')}
          autoComplete="off"
          enterKeyHint="search"
          className="min-h-[52px] w-full rounded-[14px] border border-line bg-surface px-4 text-base text-ink placeholder:text-muted/70 focus:border-primary"
        />
      </form>

      <div role="group" aria-label={t('filtersLabel')} className="flex flex-wrap gap-2">
        {chips.map((chip) => (
          <Chip key={chip.key} selected={chip.active(filters)} onClick={() => setFilters((f) => chip.toggle(f))}>
            {t(`filters.${chip.key}`)}
          </Chip>
        ))}
        {showClear ? (
          <Chip selected={false} onClick={clear} aria-label={t('clearLabel')}>
            {t('clear')}
          </Chip>
        ) : null}
      </div>

      <p role="status" className="text-sm text-muted">
        {loading ? t('loading') : t(countKey('count', results.length, locale), { n: formatNumber(results.length, locale) })}
      </p>

      {problem ? <ProblemNotice problem={problem} /> : null}

      {results.length === 0 && !loading ? (
        <EmptyState
          title={t('emptyTitle')}
          body={t('emptyBody')}
          action={
            <div className="flex flex-wrap items-center justify-center gap-3">
              {showClear ? (
                <Chip selected={false} onClick={clear}>
                  {t('emptyClear')}
                </Chip>
              ) : null}
              <Link href="/waitlist" className="inline-flex min-h-touch items-center font-semibold text-primary underline underline-offset-4">
                {t('emptyWaitlist')}
              </Link>
            </div>
          }
        />
      ) : (
        <ul aria-label={t('listLabel')} aria-busy={loading || undefined} className={`flex flex-col gap-3 ${loading ? 'opacity-60' : ''}`}>
          {results.map((therapist) => {
            const labels = specialtyLabels(tSpecialties, therapist.specialties).join(' · ');
            const next = therapist.nextAvailableAt ? formatRelativeDays(therapist.nextAvailableAt, locale, now, timeZone) : t('noSlots');
            return (
              <li key={therapist.id}>
                <Link
                  href={`/therapists/${encodeURIComponent(therapist.id)}`}
                  className="flex items-center gap-3 rounded-card border border-line bg-surface p-4"
                >
                  <Avatar initials={therapist.initials} size={48} />
                  <span className="flex min-w-0 flex-1 flex-col">
                    <span className="text-base font-bold">{therapist.displayName}</span>
                    <span className="text-sm text-muted">{labels}</span>
                  </span>
                  <span className={`shrink-0 text-sm font-semibold ${therapist.nextAvailableAt ? 'text-primary' : 'text-muted'}`}>
                    <span className="sr-only">{t('nextPrefix')}</span>
                    {next}
                  </span>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
