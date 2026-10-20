import type { ReactNode } from 'react';

/** Shared stand-ins for the API client and next-intl navigation used by the intake, crisis and safety tests. */
export const api = vi.fn();
export const router = { replace: vi.fn(), push: vi.fn(), refresh: vi.fn() };

export function Link({ href, children, ...rest }: { href: unknown; children: ReactNode; [key: string]: unknown }) {
  return (
    <a href={String(href)} {...rest}>
      {children}
    </a>
  );
}

type Handler = (body: unknown) => unknown;

/** Routes api(path, { method, body }) calls to handlers keyed "METHOD path"; unknown calls fail the test. */
export function routeApi(handlers: Record<string, Handler>) {
  api.mockImplementation(async (path: string, options?: { method?: string; body?: unknown }) => {
    const key = `${options?.method ?? 'GET'} ${path}`;
    const handler = handlers[key];
    if (!handler) throw new Error(`unexpected api call: ${key}`);
    return handler(options?.body);
  });
}

export function resetMocks() {
  api.mockReset();
  router.replace.mockReset();
  router.push.mockReset();
  router.refresh.mockReset();
  window.localStorage.clear();
}
