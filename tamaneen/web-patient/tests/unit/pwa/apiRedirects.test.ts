import { api } from '@/lib/api/client';

describe('api() system-state redirects (P35)', () => {
  const realLocation = window.location;
  const assign = vi.fn();

  beforeEach(() => {
    Object.defineProperty(window, 'location', { configurable: true, value: { ...realLocation, pathname: '/ar/appointments/a1', search: '?x=1', assign } });
    document.documentElement.lang = 'ar';
  });
  afterEach(() => {
    Object.defineProperty(window, 'location', { configurable: true, value: realLocation });
    vi.unstubAllGlobals();
    assign.mockReset();
    window.sessionStorage.clear();
  });

  const problem = (status: number, code: string) =>
    new Response(JSON.stringify({ title: 't', status, code }), { status, headers: { 'Content-Type': 'application/problem+json' } });

  it('sends an expired session to session-expired with the page to come back to', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url: string) => (url === '/api/auth/refresh' ? new Response(null, { status: 401 }) : problem(401, 'error.auth.session_revoked'))));
    await expect(api('appointments/a1')).rejects.toThrow();
    expect(assign).toHaveBeenCalledWith('/ar/session-expired?next=%2Fappointments%2Fa1%3Fx%3D1');
    expect(window.sessionStorage.getItem('tamaneen.returnTo')).toContain('/appointments/a1?x=1');
  });

  it('shows the maintenance page when the API is in scheduled maintenance', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => problem(503, 'error.maintenance')));
    await expect(api('home')).rejects.toThrow();
    expect(assign).toHaveBeenCalledWith('/ar/maintenance');
  });

  it('does not treat the crisis worker being down as maintenance', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => problem(503, 'error.crisis.unavailable')));
    await expect(api('mood', { method: 'POST', body: {} })).rejects.toThrow();
    expect(assign).not.toHaveBeenCalled();
  });
});
