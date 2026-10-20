import { Component } from '@angular/core';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { API, CRISIS, settle } from '../../testing/fixtures';
import { API_BASE_URL, CRISIS_BASE_URL } from '../api';
import type { StaffMe } from '../api/types';
import { SessionService } from '../session/session.service';
import { authInterceptor } from './auth.interceptor';
import { AuthService, REFRESH_LEAD_MS } from './auth.service';

@Component({ template: '' })
class BlankComponent {}

const ME: StaffMe = { userId: 'u-1', displayName: 'Sara A.', role: 'therapist', language: 'ar', onCall: true };

function tokens(access: string, minutes = 15) {
  return { accessToken: access, accessExpiresAt: new Date(Date.now() + minutes * 60_000).toISOString(), refreshToken: 'ignored-by-staff', refreshExpiresAt: new Date(Date.now() + 86_400_000).toISOString() };
}

function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
      provideRouter([{ path: 'login', component: BlankComponent }, { path: '**', component: BlankComponent }]),
      { provide: API_BASE_URL, useValue: API },
      { provide: CRISIS_BASE_URL, useValue: CRISIS },
    ],
  });
  return { auth: TestBed.inject(AuthService), backend: TestBed.inject(HttpTestingController), session: TestBed.inject(SessionService) };
}

describe('AuthService', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('signs in with password, then the authenticator code, and loads the profile', async () => {
    const { auth, backend, session } = setup();
    const challenge = auth.login('sara@tamaneen.test', 'correct horse battery');
    const login = backend.expectOne(`${API}/v1/auth/login`);
    expect(login.request.method).toBe('POST');
    expect(login.request.body).toEqual({ contact: 'sara@tamaneen.test', password: 'correct horse battery', client: 'staff' });
    expect(login.request.withCredentials).toBeTrue();
    login.flush({ status: 'mfa', mfaToken: 'mfa-1', methods: ['totp', 'recovery'] });
    expect((await challenge).mfaToken).toBe('mfa-1');

    const done = auth.completeMfa('mfa-1', 'totp', ' 123456 ');
    const mfa = backend.expectOne(`${API}/v1/auth/login/mfa`);
    expect(mfa.request.body).toEqual({ mfaToken: 'mfa-1', method: 'totp', code: '123456', rememberDevice: false });
    expect(mfa.request.withCredentials).toBeTrue();
    mfa.flush({ tokens: tokens('access-1'), deviceToken: null });
    await settle();

    const me = backend.expectOne(`${API}/v1/staff/me`);
    expect(me.request.headers.get('Authorization')).toBe('Bearer access-1');
    me.flush(ME);
    await done;

    expect(auth.signedIn()).toBeTrue();
    expect(auth.accessToken()).toBe('access-1');
    expect(session.roles()).toEqual(['therapist']);
    expect(session.displayName()).toBe('Sara A.');
    expect(session.crisisAccess()).toBeTrue();
    const stored = Object.keys(localStorage).map((k) => localStorage.getItem(k) ?? '').join('|') + Object.keys(sessionStorage).map((k) => sessionStorage.getItem(k) ?? '').join('|');
    expect(stored).not.toContain('access-1');
    expect(stored).not.toContain('ignored-by-staff');
  });

  it('accepts a recovery code as the second step', async () => {
    const { auth, backend } = setup();
    const done = auth.completeMfa('mfa-1', 'recovery', 'ABCD-EFGH');
    const mfa = backend.expectOne(`${API}/v1/auth/login/mfa`);
    expect(mfa.request.body).toEqual({ mfaToken: 'mfa-1', method: 'recovery', code: 'ABCD-EFGH', rememberDevice: false });
    mfa.flush({ tokens: tokens('a'), deviceToken: null });
    await settle();
    backend.expectOne(`${API}/v1/staff/me`).flush(ME);
    expect((await done).displayName).toBe('Sara A.');
  });

  it('refreshes from the cookie 60 s before the access token expires, with an empty body', fakeAsync(() => {
    const { auth, backend } = setup();
    void auth.completeMfa('mfa-1', 'totp', '123456');
    backend.expectOne(`${API}/v1/auth/login/mfa`).flush({ tokens: tokens('access-1', 15), deviceToken: null });
    flushMicrotasks();
    backend.expectOne(`${API}/v1/staff/me`).flush(ME);
    flushMicrotasks();

    tick(15 * 60_000 - REFRESH_LEAD_MS - 1);
    backend.expectNone(`${API}/v1/auth/refresh`);
    tick(1);
    const refresh = backend.expectOne(`${API}/v1/auth/refresh`);
    expect(refresh.request.body).toEqual({});
    expect(refresh.request.withCredentials).toBeTrue();
    refresh.flush(tokens('access-2', 15));
    flushMicrotasks();
    expect(auth.accessToken()).toBe('access-2');

    // The next one is scheduled from the new expiry; when the server refuses it, the session ends.
    tick(15 * 60_000 - REFRESH_LEAD_MS);
    backend.expectOne(`${API}/v1/auth/refresh`).flush({ title: 'Session ended', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    flushMicrotasks();
    tick();
    expect(auth.signedIn()).toBeFalse();
    expect(TestBed.inject(Router).url).toBe('/login');
  }));

  it('restores a session silently on start, and stays signed out without a cookie', async () => {
    const { auth, backend, session } = setup();
    const restored = auth.restore();
    backend.expectOne(`${API}/v1/auth/refresh`).flush(tokens('access-r'));
    await settle();
    backend.expectOne(`${API}/v1/staff/me`).flush({ ...ME, role: 'supervisor', onCall: false });
    expect(await restored).toBeTrue();
    expect(session.role()).toBe('supervisor');

    auth['clearLocal']();
    const second = auth.restore();
    backend.expectOne(`${API}/v1/auth/refresh`).flush({ title: 'No session', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    expect(await second).toBeFalse();
    expect(auth.signedIn()).toBeFalse();
  });

  it('shares one refresh between concurrent callers', async () => {
    const { auth, backend } = setup();
    const a = auth.refresh();
    const b = auth.refresh();
    backend.expectOne(`${API}/v1/auth/refresh`).flush(tokens('shared'));
    expect(await a).toBe('shared');
    expect(await b).toBe('shared');
  });

  it('signs out on the server with credentials, then clears the session', async () => {
    const { auth, backend, session } = setup();
    void auth.restore();
    backend.expectOne(`${API}/v1/auth/refresh`).flush(tokens('access-1'));
    await settle();
    backend.expectOne(`${API}/v1/staff/me`).flush(ME);
    await settle();

    const out = auth.logout();
    const logout = backend.expectOne(`${API}/v1/auth/logout`);
    expect(logout.request.body).toEqual({});
    expect(logout.request.withCredentials).toBeTrue();
    logout.flush(null, { status: 204, statusText: 'No Content' });
    await out;
    expect(auth.signedIn()).toBeFalse();
    expect(auth.accessToken()).toBeNull();
    expect(session.role()).toBeNull();
    expect(TestBed.inject(Router).url).toBe('/login');
  });
});

describe('authInterceptor', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  async function signedIn() {
    const ctx = setup();
    void ctx.auth.restore();
    ctx.backend.expectOne(`${API}/v1/auth/refresh`).flush(tokens('access-1'));
    await settle();
    ctx.backend.expectOne(`${API}/v1/staff/me`).flush(ME);
    await settle();
    return { ...ctx, http: TestBed.inject(HttpClient) };
  }

  it('adds the token to the API and the crisis worker only', async () => {
    const { http, backend } = await signedIn();
    http.get(`${API}/v1/staff/dashboard`).subscribe();
    http.get(`${CRISIS}/v1/crisis/health`).subscribe();
    http.get('./assets/i18n/en.json').subscribe();
    http.get(`${API}.evil.example/v1/steal`).subscribe();
    http.get('https://fonts.googleapis.com/css2').subscribe();
    expect(backend.expectOne(`${API}/v1/staff/dashboard`).request.headers.get('Authorization')).toBe('Bearer access-1');
    expect(backend.expectOne(`${CRISIS}/v1/crisis/health`).request.headers.get('Authorization')).toBe('Bearer access-1');
    expect(backend.expectOne('./assets/i18n/en.json').request.headers.has('Authorization')).toBeFalse();
    expect(backend.expectOne(`${API}.evil.example/v1/steal`).request.headers.has('Authorization')).toBeFalse();
    expect(backend.expectOne('https://fonts.googleapis.com/css2').request.headers.has('Authorization')).toBeFalse();
  });

  it('refreshes once on a 401 and retries the request once with the new token', async () => {
    const { http, backend } = await signedIn();
    let result: unknown;
    http.get(`${CRISIS}/v1/crisis/events/open`).subscribe((r) => (result = r));
    backend.expectOne(`${CRISIS}/v1/crisis/events/open`).flush({ title: 'Expired', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne(`${API}/v1/auth/refresh`).flush(tokens('access-2'));
    await settle();
    const retry = backend.expectOne(`${CRISIS}/v1/crisis/events/open`);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer access-2');
    retry.flush([]);
    expect(result).toEqual([]);
  });

  it('does not retry a second time: a repeated 401 ends the session', async () => {
    const { http, backend, auth } = await signedIn();
    let status = 0;
    http.get(`${API}/v1/staff/dashboard`).subscribe({ error: (e: { status: number }) => (status = e.status) });
    backend.expectOne(`${API}/v1/staff/dashboard`).flush({ title: 'Expired', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne(`${API}/v1/auth/refresh`).flush(tokens('access-2'));
    await settle();
    backend.expectOne(`${API}/v1/staff/dashboard`).flush({ title: 'Still no', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    await settle();
    backend.expectNone(`${API}/v1/auth/refresh`);
    backend.expectNone(`${API}/v1/staff/dashboard`);
    expect(status).toBe(401);
    expect(auth.signedIn()).toBeFalse();
  });

  it('passes the 401 on and signs out when the refresh is refused', async () => {
    const { http, backend, auth } = await signedIn();
    let status = 0;
    http.get(`${API}/v1/staff/dashboard`).subscribe({ error: (e: { status: number }) => (status = e.status) });
    backend.expectOne(`${API}/v1/staff/dashboard`).flush({ title: 'Expired', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne(`${API}/v1/auth/refresh`).flush({ title: 'No session', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    await settle();
    expect(status).toBe(401);
    expect(auth.signedIn()).toBeFalse();
  });

  it('does not refresh on other errors', async () => {
    const { http, backend } = await signedIn();
    let status = 0;
    http.post(`${CRISIS}/v1/crisis/events/ev-1/acknowledge`, {}).subscribe({ error: (e: { status: number }) => (status = e.status) });
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/acknowledge`).flush({ title: 'Taken', status: 409 }, { status: 409, statusText: 'Conflict' });
    backend.expectNone(`${API}/v1/auth/refresh`);
    expect(status).toBe(409);
  });
});
