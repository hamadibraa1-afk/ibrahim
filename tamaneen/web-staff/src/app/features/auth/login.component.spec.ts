import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { API_BASE_URL } from '../../core/api';
import { LanguageService } from '../../core/i18n/language.service';
import { settle } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { LoginComponent } from './login.component';

@Component({ template: '' })
class BlankComponent {}

describe('LoginComponent', () => {
  let backend: HttpTestingController;

  async function render() {
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideTestTranslations(), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([{ path: '**', component: BlankComponent }]), { provide: API_BASE_URL, useValue: '/api' }],
    });
    await TestBed.inject(LanguageService).use('en');
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const type = (testId: string, value: string) => {
      const input = el.querySelector(`[data-testid="${testId}"]`) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    const click = (testId: string) => (el.querySelector(`[data-testid="${testId}"]`) as HTMLButtonElement).click();
    const update = async () => {
      await settle();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, el, type, click, update };
  }

  async function toMfa(ctx: Awaited<ReturnType<typeof render>>) {
    ctx.type('contact', 'sara@tamaneen.test');
    ctx.type('password', 'correct horse battery');
    ctx.click('continue');
    backend.expectOne('/api/v1/auth/login').flush({ status: 'mfa', mfaToken: 'mfa-1', methods: ['totp', 'recovery'] });
    await ctx.update();
  }

  afterEach(() => {
    backend.verify();
    localStorage.clear();
  });

  it('asks for the authenticator code after the password, then lands on the home of the role', async () => {
    const ctx = await render();
    await toMfa(ctx);
    expect(ctx.el.querySelector('h1')!.textContent).toContain('Two-step verification');
    expect(document.activeElement).toBe(ctx.el.querySelector('[data-testid="code"]'));

    ctx.type('code', '123456');
    ctx.click('verify');
    backend.expectOne('/api/v1/auth/login/mfa').flush({ accessToken: 'a', accessExpiresAt: new Date(Date.now() + 900_000).toISOString() });
    await settle();
    backend.expectOne('/api/v1/staff/me').flush({ userId: 'u', displayName: 'Huda', role: 'lead', language: 'en', onCall: false });
    await ctx.update();
    expect(TestBed.inject(Router).url).toBe('/crisis');
  });

  it('shows the server problem with the attempts left', async () => {
    const ctx = await render();
    ctx.type('contact', 'sara@tamaneen.test');
    ctx.type('password', 'wrong');
    ctx.click('continue');
    backend.expectOne('/api/v1/auth/login').flush({ title: 'Wrong email or password', status: 401, remainingAttempts: 3 }, { status: 401, statusText: 'Unauthorized' });
    await ctx.update();
    const error = ctx.el.querySelector('[data-testid="login-error"]')!;
    expect(error.getAttribute('role')).toBe('alert');
    expect(error.textContent).toContain('Wrong email or password');
    expect(error.textContent).toContain('Attempts left before the account is locked: 3.');
  });

  it('explains the lock with the minutes to wait', async () => {
    const ctx = await render();
    await toMfa(ctx);
    ctx.type('code', '000000');
    ctx.click('verify');
    backend.expectOne('/api/v1/auth/login/mfa').flush({ title: 'Account locked', status: 423, retryAfterSeconds: 870 }, { status: 423, statusText: 'Locked' });
    await ctx.update();
    expect(ctx.el.querySelector('[data-testid="login-error"]')!.textContent).toContain('Try again in 15 minutes.');
  });

  it('validates the 6-digit code and can switch to a recovery code', async () => {
    const ctx = await render();
    await toMfa(ctx);
    ctx.type('code', '12a');
    ctx.click('verify');
    await ctx.update();
    backend.expectNone('/api/v1/auth/login/mfa');
    expect(ctx.el.textContent).toContain('Enter the 6 digits');

    ctx.click('switch-method');
    await ctx.update();
    expect(ctx.el.textContent).toContain('Recovery code');
    ctx.type('code', 'ABCD-EFGH');
    ctx.click('verify');
    expect(backend.expectOne('/api/v1/auth/login/mfa').request.body).toEqual({ mfaToken: 'mfa-1', method: 'recovery', code: 'ABCD-EFGH', rememberDevice: false });
  });

  it('shows a calm message when the server cannot be reached', async () => {
    const ctx = await render();
    ctx.type('contact', 'sara@tamaneen.test');
    ctx.type('password', 'x');
    ctx.click('continue');
    backend.expectOne('/api/v1/auth/login').error(new ProgressEvent('error'));
    await ctx.update();
    expect(ctx.el.querySelector('[data-testid="login-error"]')!.textContent).toContain('Crisis alerts still arrive');
  });
});
