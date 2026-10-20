import { cookies } from 'next/headers';
import { NextResponse, type NextRequest } from 'next/server';
import { ACCESS_TOKEN_COOKIE, LOCALE_COOKIE, downstreamHeaders, resolveLocale, upstreamHeaders, upstreamUrl } from '@/lib/bff';

/**
 * Backend-for-frontend proxy. The browser never sees the access token: it stays in an HttpOnly cookie
 * and is attached here. Accept-Language comes from the user's language choice so API messages and
 * errors arrive in the language on screen.
 */
export const dynamic = 'force-dynamic';

const TIMEOUT_MS = 20_000;

async function proxy(request: NextRequest, { params }: { params: { path: string[] } }) {
  const apiBase = process.env.TAMANEEN_API_URL;
  if (!apiBase) {
    return NextResponse.json({ title: 'API is not configured', status: 503 }, { status: 503 });
  }

  let url: URL;
  try {
    url = upstreamUrl(apiBase, params.path, request.nextUrl.search);
  } catch {
    return NextResponse.json({ title: 'Not found', status: 404 }, { status: 404 });
  }

  const jar = cookies();
  const locale = resolveLocale(jar.get(LOCALE_COOKIE)?.value, request.headers.get('accept-language'));
  const hasBody = !['GET', 'HEAD'].includes(request.method);
  let upstream: Response;
  try {
    upstream = await fetch(url, {
      method: request.method,
      headers: upstreamHeaders({
        locale,
        accessToken: jar.get(ACCESS_TOKEN_COOKIE)?.value,
        contentType: request.headers.get('content-type'),
        requestId: request.headers.get('x-request-id'),
      }),
      body: hasBody ? await request.arrayBuffer() : undefined,
      cache: 'no-store',
      redirect: 'manual',
    signal: AbortSignal.timeout(TIMEOUT_MS),
    });
  } catch {
    // The page shows its own localized message and the Help now button stays available.
    return NextResponse.json({ title: 'Upstream unavailable', status: 502 }, { status: 502 });
  }

  return new NextResponse(upstream.body, { status: upstream.status, headers: downstreamHeaders(upstream.headers) });
}

export { proxy as GET, proxy as POST, proxy as PUT, proxy as PATCH, proxy as DELETE };
