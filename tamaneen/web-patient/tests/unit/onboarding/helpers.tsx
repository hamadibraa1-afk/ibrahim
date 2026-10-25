import type { AnchorHTMLAttributes, ReactNode } from 'react';
import { ApiError } from '@/lib/api/errors';
import type { ApiProblem } from '@/lib/api/types';

/** Shared router spies. Test files mock '@/i18n/navigation' with navigationMock(). */
export const router = { push: vi.fn(), replace: vi.fn(), back: vi.fn(), refresh: vi.fn() };

type Href = string | { pathname: string; query?: Record<string, string> };

export function navigationMock() {
  return {
    useRouter: () => router,
    usePathname: () => '/',
    Link: ({ href, children, locale: _locale, ...rest }: { href: Href; children: ReactNode; locale?: string } & Omit<AnchorHTMLAttributes<HTMLAnchorElement>, 'href'>) => {
      const url = typeof href === 'string' ? href : `${href.pathname}${href.query ? `?${new URLSearchParams(href.query)}` : ''}`;
      return (
        <a href={url} {...rest}>
          {children}
        </a>
      );
    },
  };
}

/** '@/lib/api/client' with api/authApi as spies and the real ApiError (so instanceof works). */
export function clientMock() {
  const api = vi.fn();
  return { api, authApi: vi.fn(), ApiError, apiVersioned: versionedVia(api), isVersionConflict };
}

/** Every If-Match the forms sent, as (path, value). */
export const ifMatchSent = vi.fn();

/** The ETag the mocked apiVersioned answers with. */
export const VERSION = '"v2"';

/**
 * apiVersioned on top of an api spy: the spy sees the same (path, { method, body }) as before, the If-Match
 * goes to ifMatchSent, and every response carries VERSION as its ETag.
 */
export function versionedVia(api: (...args: unknown[]) => unknown) {
  return async (path: string, options?: { ifMatch?: string | null; [key: string]: unknown }) => {
    const { ifMatch, ...rest } = options ?? {};
    ifMatchSent(path, ifMatch ?? null);
    const data = await (options ? api(path, rest) : api(path));
    return { data, etag: VERSION };
  };
}

export function isVersionConflict(e: unknown): boolean {
  return e instanceof ApiError && e.status === 412;
}

export function apiError(status: number, extra: Partial<ApiProblem> = {}): ApiError {
  return new ApiError(status, { title: extra.title ?? 'Problem', status, ...extra });
}

export function resetAll() {
  for (const fn of Object.values(router)) fn.mockReset();
  ifMatchSent.mockReset();
  window.sessionStorage.clear();
}
