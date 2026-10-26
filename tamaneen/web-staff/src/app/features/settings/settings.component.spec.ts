import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { API_BASE_URL } from '../../core/api';
import type { StaffSecurity } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { API } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { SettingsComponent } from './settings.component';

const security: StaffSecurity = {
  passwordChangedAt: '2026-09-25T10:00:00Z', totpEnabled: true, recoveryCodesRemaining: 8, idleTimeoutMinutes: 15,
  sessions: [
    { id: 's0', deviceLabel: 'Chrome · Mac', current: true, lastActiveAt: '2026-10-25T10:00:00Z' },
    { id: 's1', deviceLabel: 'Chrome · Windows', current: false, lastActiveAt: '2026-10-24T10:00:00Z' },
  ],
};

describe('SettingsComponent (S32)', () => {
  let backend: HttpTestingController;

  async function render(language: 'ar' | 'en' = 'en') {
    TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        provideTestTranslations(), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), { provide: API_BASE_URL, useValue: API },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ session: 's1' }) } } },
      ],
    });
    await TestBed.inject(LanguageService).use(language);
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();
    backend.expectOne(`${API}/v1/staff/me/security`).flush(security);
    backend.expectOne(`${API}/v1/staff/me/notifications`).flush({ riskAlerts: true, bookings: true, weeklyReport: true, dailyDigest: false });
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => {
    backend.verify();
    localStorage.clear();
  });

  it('locks risk alerts on, flags the session from S41 and ends it', async () => {
    const { el } = await render();
    expect((el.querySelector('[data-testid="risk-alerts"] button') as HTMLButtonElement).disabled).toBeTrue();
    expect(el.textContent).toContain('a session ends after 15 minutes idle');
    const sessions = el.querySelectorAll('[data-testid="session"]');
    expect(sessions[1].classList).toContain('flagged');
    (el.querySelector('[data-testid="end-s1"]') as HTMLButtonElement).click();
    backend.expectOne((r) => r.method === 'DELETE' && r.url === `${API}/v1/staff/me/sessions/s1`).flush(null);
    backend.expectOne(`${API}/v1/staff/me/security`).flush({ ...security, sessions: [security.sessions[0]] });
  });

  it('puts a switch back when its save fails', async () => {
    const { fixture, el } = await render();
    (el.querySelector('[data-testid="pref-bookings"] button') as HTMLButtonElement).click();
    const req = backend.expectOne(`${API}/v1/staff/me/notifications`);
    expect(req.request.body).toEqual({ riskAlerts: true, bookings: false, weeklyReport: true, dailyDigest: false });
    req.flush({ title: 'x', status: 500 }, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="pref-bookings"] button')!.getAttribute('aria-checked')).toBe('true');
  });

  it('moves the authenticator in two steps after a step-up', async () => {
    const { fixture, el } = await render('ar');
    (el.querySelector('[data-testid="move-totp"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    const form = el.querySelector('[data-testid="move-stepup"]') as HTMLFormElement;
    const inputs = form.querySelectorAll('input');
    inputs[0].value = 'pw'; inputs[0].dispatchEvent(new Event('input'));
    inputs[1].value = '123456'; inputs[1].dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    backend.expectOne(`${API}/v1/staff/me/totp/move`).flush({ secret: 'ABCDEF', otpauthUri: 'otpauth://totp/x', moveToken: 'tok', expiresAt: '2026-10-25T10:10:00Z' });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="move-secret"]')!.textContent).toContain('ABCDEF');
    const confirm = el.querySelector('[data-testid="move-confirm"]') as HTMLFormElement;
    const code = confirm.querySelector('input')!;
    code.value = '654321'; code.dispatchEvent(new Event('input'));
    confirm.dispatchEvent(new Event('submit'));
    const req = backend.expectOne(`${API}/v1/staff/me/totp/move/confirm`);
    expect(req.request.body).toEqual({ moveToken: 'tok', code: '654321' });
    req.flush(null);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="notice"]')!.textContent).toContain('نُقل تطبيق المصادقة');
  });

  it('switches the interface language and tells the server', async () => {
    const { el } = await render('ar');
    (el.querySelector('[data-testid="lang-en"] button') as HTMLButtonElement).click();
    await new Promise((r) => setTimeout(r));
    backend.expectOne((r) => r.method === 'PUT' && r.url === `${API}/v1/me/language`).flush({});
    expect(TestBed.inject(LanguageService).language()).toBe('en');
    expect(document.documentElement.dir).toBe('ltr');
  });
});
