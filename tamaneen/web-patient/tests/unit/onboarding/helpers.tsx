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
  return { api: vi.fn(), authApi: vi.fn(), ApiError };
}

export function apiError(status: number, extra: Partial<ApiProblem> = {}): ApiError {
  return new ApiError(status, { title: extra.title ?? 'Problem', status, ...extra });
}

export function resetAll() {
  for (const fn of Object.values(router)) fn.mockReset();
  window.sessionStorage.clear();
}
