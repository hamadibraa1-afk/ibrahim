import { rememberReturnPath, safeReturnPath, sessionExpiredUrl, takeReturnPath } from '@/lib/returnPath';

const mocks = vi.hoisted(() => ({ api: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api }));

import { signedInPath } from '@/components/auth/finishSignIn';

const me = (onboarding: Record<string, unknown> = {}) => ({
  pseudonymId: 'P-1',
  nickname: 'نور',
  language: 'ar',
  timeZone: 'Asia/Riyadh',
  onboarding: { consentsDone: true, safetyInfoDone: true, path: 'direct', screeningDone: true, intakeStatus: 'none', triageStatus: 'matched', ...onboarding },
  chatLocked: false,
});

afterEach(() => {
  window.sessionStorage.clear();
  mocks.api.mockReset();
});

describe('safeReturnPath', () => {
  it('keeps signed-in paths and their query, without the locale prefix', () => {
    expect(safeReturnPath('/ar/appointments')).toBe('/appointments');
    expect(safeReturnPath('/en/therapists?specialty=anxiety')).toBe('/therapists?specialty=anxiety');
    expect(safeReturnPath('/appointments/a1/')).toBe('/appointments/a1');
  });

  it('refuses other sites, odd input and sign-in pages', () => {
    for (const bad of ['https://evil.example/x', '//evil.example/x', '/\\evil.example', 'appointments', '', null, '/ar/login', '/login/mfa', '/en/session-expired', '/ar', '/', `/${'a'.repeat(400)}`]) {
      expect(safeReturnPath(bad)).toBeNull();
    }
  });

  it('builds the session-expired URL with the encoded path', () => {
    expect(sessionExpiredUrl('ar', '/ar/appointments?x=1')).toBe('/ar/session-expired?next=%2Fappointments%3Fx%3D1');
    expect(sessionExpiredUrl('en', '/en/login')).toBe('/en/session-expired');
  });
});

describe('return after signing in again (P35)', () => {
  it('remembers the path for this tab once, for 30 minutes', () => {
    rememberReturnPath('/ar/homework', 1000);
    expect(takeReturnPath(1000 + 30 * 60 * 1000)).toBe('/homework');
    expect(takeReturnPath()).toBeNull();
    rememberReturnPath('/ar/homework', 1000);
    expect(takeReturnPath(1001 + 30 * 60 * 1000)).toBeNull();
  });

  it('sends a finished patient back to the remembered page', async () => {
    mocks.api.mockResolvedValue(me());
    rememberReturnPath('/ar/appointments/a1');
    expect(await signedInPath()).toBe('/appointments/a1');
  });

  it('lets unfinished onboarding win over the remembered page', async () => {
    mocks.api.mockResolvedValue(me({ consentsDone: false }));
    rememberReturnPath('/ar/appointments/a1');
    expect(await signedInPath()).toBe('/start/consents');
    expect(takeReturnPath()).toBeNull();
  });

  it('lands on home without a remembered page', async () => {
    mocks.api.mockResolvedValue(me());
    expect(await signedInPath()).toBe('/home');
  });
});
