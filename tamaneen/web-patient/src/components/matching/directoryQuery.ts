import type { Gender, Lang } from '@/lib/api/types';

/** P24 filters. They live in the URL (q, specialty, language, gender, evening) so back and refresh keep them. */
export interface DirectoryFilters {
  q: string;
  specialty: string | null;
  language: Lang | null;
  gender: Gender | null;
  evening: boolean;
}

export const emptyFilters: DirectoryFilters = { q: '', specialty: null, language: null, gender: null, evening: false };

type ParamSource = URLSearchParams | Record<string, string | string[] | undefined>;

function read(source: ParamSource, key: string): string | undefined {
  if (source instanceof URLSearchParams) return source.get(key) ?? undefined;
  const value = source[key];
  return Array.isArray(value) ? value[0] : value;
}

/** Reads filters from page searchParams or a URLSearchParams; anything unexpected is ignored. */
export function parseFilters(source: ParamSource): DirectoryFilters {
  const specialty = read(source, 'specialty');
  const language = read(source, 'language');
  const gender = read(source, 'gender');
  const evening = read(source, 'evening');
  return {
    q: (read(source, 'q') ?? '').trim().slice(0, 100),
    specialty: specialty && /^[a-z_]{1,40}$/.test(specialty) ? specialty : null,
    language: language === 'ar' || language === 'en' ? language : null,
    gender: gender === 'female' || gender === 'male' ? gender : null,
    evening: evening === 'true' || evening === '1',
  };
}

/** The same query string for the page URL and for GET /v1/therapists. Empty when nothing is filtered. */
export function filtersToQuery(filters: DirectoryFilters): string {
  const params = new URLSearchParams();
  if (filters.q.trim()) params.set('q', filters.q.trim());
  if (filters.specialty) params.set('specialty', filters.specialty);
  if (filters.language) params.set('language', filters.language);
  if (filters.gender) params.set('gender', filters.gender);
  if (filters.evening) params.set('evening', 'true');
  return params.toString();
}

export function hasFilters(filters: DirectoryFilters): boolean {
  return filtersToQuery(filters) !== '';
}

export function therapistsPath(filters: DirectoryFilters): string {
  const query = filtersToQuery(filters);
  return query ? `therapists?${query}` : 'therapists';
}
