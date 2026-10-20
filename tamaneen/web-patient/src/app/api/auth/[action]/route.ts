import { cookies } from 'next/headers';
import { NextResponse, type NextRequest } from 'next/server';
import type { LoginResponse, MfaResponse, SignupVerifiedResponse, TokenPair } from '@/lib/api/types';
import { callApi, resolveLocale } from '@/lib/api/upstream';
import { DEVICE_COOKIE, REFRESH_COOKIE, deviceCookie } from '@/lib/auth/cookies';
import { clearSessionCookies, setSessionCookies } from '@/lib/auth/session';
import { LOCALE_COOKIE } from '@/lib/bff';

/**
 * Sign-in steps that return tokens. The tokens go into HttpOnly cookies here and the browser only gets
 * what the next screen needs (recovery codes, the MFA step), so script on the page can never read them.
 */
export const dynamic = 'force-dynamic';

const ACTIONS = new Set(['signup-verify', 'login', 'login-mfa', 'refresh', 'logout']);

function passthrough(upstream: Response, body: unknown): NextResponse {
  return NextResponse.json(body, { status: upstream.status, headers: { 'content-language': upstream.headers.get('content-language') ?? '' } });
}

export async function POST(request: NextRequest, { params }: { params: Promise<{ action: string }> }) {
  const { action } = await params;
  if (!ACTIONS.has(action)) return NextResponse.json({ title: 'Not found', status: 404 }, { status: 404 });
  if (!process.env.TAMANEEN_API_URL) return NextResponse.json({ title: 'API is not configured', status: 503 }, { status: 503 });

  // Same-origin only: these routes set the session, so a cross-site form must not reach them.
  const origin = request.headers.get('origin');
  if (origin && origin !== request.nextUrl.origin) return NextResponse.json({ title: 'Forbidden', status: 403 }, { status: 403 });

  const jar = await cookies();
  const locale = resolveLocale(jar.get(LOCALE_COOKIE)?.value, request.headers.get('accept-language'));
  const input = action === 'refresh' || action === 'logout' ? {} : ((await request.json().catch(() => ({}))) as Record<string, unknown>);
  const call = (path: string, body: unknown) => callApi(path, { method: 'POST', body, locale, requestId: request.headers.get('x-request-id') });

  let upstream: Response;
  try {
    switch (action) {
      case 'signup-verify':
        upstream = await call('v1/auth/signup/verify', input);
        break;
      case 'login':
        upstream = await call('v1/auth/login', { ...input, client: 'patient', deviceToken: jar.get(DEVICE_COOKIE)?.value });
        break;
      case 'login-mfa':
        upstream = await call('v1/auth/login/mfa', input);
        break;
      case 'refresh': {
        const refreshToken = jar.get(REFRESH_COOKIE)?.value;
        if (!refreshToken) {
          const res = NextResponse.json({ title: 'Unauthorized', status: 401 }, { status: 401 });
          clearSessionCookies(res);
          return res;
        }
        upstream = await call('v1/auth/refresh', { refreshToken });
        break;
      }
      default: {
        const refreshToken = jar.get(REFRESH_COOKIE)?.value;
        if (refreshToken) await call('v1/auth/logout', { refreshToken }).catch(() => undefined);
        const res = new NextResponse(null, { status: 204 });
        clearSessionCookies(res);
        return res;
      }
    }
  } catch {
    return NextResponse.json({ title: 'Service unavailable', status: 502 }, { status: 502 });
  }

  const body = (await upstream.json().catch(() => ({}))) as Record<string, unknown>;
  if (!upstream.ok) {
    const res = passthrough(upstream, body);
    if (action === 'refresh') clearSessionCookies(res);
    return res;
  }

  switch (action) {
    case 'signup-verify': {
      const data = body as unknown as SignupVerifiedResponse;
      const res = NextResponse.json({ recoveryCodes: data.recoveryCodes });
      setSessionCookies(res, data.tokens);
      return res;
    }
    case 'login': {
      const data = body as unknown as LoginResponse;
      if (data.status === 'mfa') return NextResponse.json(data);
      const res = NextResponse.json({ status: 'ok' });
      setSessionCookies(res, data.tokens);
      return res;
    }
    case 'login-mfa': {
      const data = body as unknown as MfaResponse;
      const res = NextResponse.json({ status: 'ok' });
      setSessionCookies(res, data.tokens);
      if (data.deviceToken) res.cookies.set(DEVICE_COOKIE, data.deviceToken, deviceCookie());
      return res;
    }
    default: {
      const res = new NextResponse(null, { status: 204 });
      setSessionCookies(res, body as unknown as TokenPair);
      return res;
    }
  }
}
