import { downstreamHeaders, resolveLocale, upstreamHeaders, upstreamUrl } from '@/lib/bff';
import { publicPlatformConfig } from '@/lib/platform-config';

describe('BFF language negotiation', () => {
  it('prefers the saved choice over the browser', () => {
    expect(resolveLocale('en', 'ar-SA,ar;q=0.9')).toBe('en');
  });

  it('falls back to the browser, then Arabic', () => {
    expect(resolveLocale(undefined, 'en-GB,en;q=0.8')).toBe('en');
    expect(resolveLocale(undefined, 'fr-FR,de;q=0.5')).toBe('ar');
    expect(resolveLocale('xx', null)).toBe('ar');
  });

  it('sends Accept-Language and the token upstream, never a cookie', () => {
    const h = upstreamHeaders({ locale: 'en', accessToken: 'abc', contentType: 'application/json' });
    expect(h.get('accept-language')).toBe('en');
    expect(h.get('authorization')).toBe('Bearer abc');
    expect(h.get('cookie')).toBeNull();
  });

  it('forwards only safe response headers', () => {
    const upstream = new Headers({ 'content-type': 'application/problem+json', 'content-language': 'ar', 'set-cookie': 'x=1', server: 'kestrel' });
    const h = downstreamHeaders(upstream);
    expect(h.get('content-language')).toBe('ar');
    expect(h.get('set-cookie')).toBeNull();
    expect(h.get('server')).toBeNull();
  });
});

describe('BFF path safety', () => {
  it('builds /v1 URLs', () => {
    expect(upstreamUrl('https://api.example', ['me', 'language'], '?a=1').toString()).toBe('https://api.example/v1/me/language?a=1');
  });

  it.each([[['..', 'admin']], [['me', '%2F..']], [[]], [['a/b']]])('rejects %j', (segments) => {
    expect(() => upstreamUrl('https://api.example', segments, '')).toThrow(RangeError);
  });
});

describe('public platform config', () => {
  it('accepts a valid emergency number and hours', () => {
    expect(publicPlatformConfig({ TAMANEEN_EMERGENCY_NUMBER: '911', TAMANEEN_MATCH_WITHIN_HOURS: '48' } as unknown as NodeJS.ProcessEnv)).toMatchObject({ emergencyNumber: '911', matchWithinHours: 48 });
  });

  it('ignores invalid values instead of showing them', () => {
    expect(publicPlatformConfig({ TAMANEEN_EMERGENCY_NUMBER: 'call me', TAMANEEN_MATCH_WITHIN_HOURS: '-3' } as unknown as NodeJS.ProcessEnv)).toMatchObject({ emergencyNumber: null, matchWithinHours: 72 });
  });
});
