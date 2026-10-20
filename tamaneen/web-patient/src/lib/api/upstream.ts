import { resolveLocale, upstreamHeaders } from '@/lib/bff';
import type { Locale } from '@/i18n/routing';

/** Server-side call to the .NET API. Throws if TAMANEEN_API_URL is not configured. */
export async function callApi(
  path: string,
  init: { method?: string; body?: unknown; locale: Locale; accessToken?: string; requestId?: string | null },
): Promise<Response> {
  const base = process.env.TAMANEEN_API_URL;
  if (!base) throw new Error('TAMANEEN_API_URL is not configured');
  const url = new URL(path.replace(/^\/+/, ''), base.endsWith('/') ? base : `${base}/`);
  return fetch(url, {
    method: init.method ?? 'GET',
    headers: upstreamHeaders({
      locale: init.locale,
      accessToken: init.accessToken,
      contentType: init.body === undefined ? null : 'application/json',
      requestId: init.requestId,
    }),
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
    cache: 'no-store',
    redirect: 'manual',
    signal: AbortSignal.timeout(20_000),
  });
}

export { resolveLocale };
