import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { API_BASE_URL } from '../../core/api';
import { LanguageService } from '../../core/i18n/language.service';
import { settle } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { RecoverComponent } from './recover.component';

@Component({ template: '' })
class BlankComponent {}

describe('RecoverComponent', () => {
  let backend: HttpTestingController;

  async function render(language: 'ar' | 'en' = 'en') {
    TestBed.configureTestingModule({
      imports: [RecoverComponent],
      providers: [provideTestTranslations(), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([{ path: '**', component: BlankComponent }]), { provide: API_BASE_URL, useValue: '/api' }],
    });
    await TestBed.inject(LanguageService).use(language);
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(RecoverComponent);
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

  afterEach(() => {
    backend.verify();
    localStorage.clear();
  });

  it('sends a code to the work email, then sets the new password with it', async () => {
    const ctx = await render();
    expect(ctx.el.querySelector('h1')!.textContent).toContain('Recover your account');
    expect(ctx.el.querySelector('[data-testid="mfa-note"]')!.textContent).toContain('two admins');

    ctx.type('recover-contact', ' sara@staff.tamaneen.test ');
    ctx.click('send-code');
    const start = backend.expectOne('/api/v1/auth/recover');
    expect(start.request.body).toEqual({ contact: 'sara@staff.tamaneen.test' });
    expect(start.request.withCredentials).toBeTrue();
    start.flush({ challengeId: 'ch-1', maskedContact: 's***@staff.tamaneen.test', resendAfterSeconds: 60, expiresInSeconds: 600 });
    await ctx.update();
    expect(ctx.el.querySelector('[data-testid="recover-sent"]')!.textContent).toContain('s***@staff.tamaneen.test');

    ctx.type('recover-code', '123456');
    ctx.type('recover-password', 'short');
    ctx.click('reset-password');
    await ctx.update();
    backend.expectNone('/api/v1/auth/recover/complete');
    expect(ctx.el.textContent).toContain('At least 12 characters');

    ctx.type('recover-password', 'Quiet-Garden-Path-2026');
    ctx.click('reset-password');
    const complete = backend.expectOne('/api/v1/auth/recover/complete');
    expect(complete.request.body).toEqual({ challengeId: 'ch-1', code: '123456', newPassword: 'Quiet-Garden-Path-2026' });
    complete.flush(null, { status: 204, statusText: 'No Content' });
    await ctx.update();
    expect(ctx.el.querySelector('[data-testid="recover-done"]')!.textContent).toContain('signed out on every device');
    expect(ctx.el.querySelector('[data-testid="back-to-login"]')!.getAttribute('href')).toBe('/login');
  });

  it('shows the server problem for a wrong code and clears the code field', async () => {
    const ctx = await render();
    ctx.type('recover-contact', 'sara@staff.tamaneen.test');
    ctx.click('send-code');
    backend.expectOne('/api/v1/auth/recover').flush({ challengeId: 'ch-1', maskedContact: 's***', resendAfterSeconds: 60, expiresInSeconds: 600 });
    await ctx.update();
    ctx.type('recover-code', '000000');
    ctx.type('recover-password', 'Quiet-Garden-Path-2026');
    ctx.click('reset-password');
    backend.expectOne('/api/v1/auth/recover/complete').flush({ title: 'The code is not right', status: 400, remainingAttempts: 4 }, { status: 400, statusText: 'Bad Request' });
    await ctx.update();
    const error = ctx.el.querySelector('[data-testid="recover-error"]')!;
    expect(error.textContent).toContain('The code is not right');
    expect(error.textContent).toContain('Attempts left for this code: 4.');
    expect((ctx.el.querySelector('[data-testid="recover-code"]') as HTMLInputElement).value).toBe('');
  });

  it('validates the email (format and 254 characters) before calling the API, in Arabic too', async () => {
    const ctx = await render('ar');
    expect(ctx.el.querySelector('[data-testid="recover"]')!.getAttribute('dir')).toBe('rtl');
    ctx.type('recover-contact', `${'a'.repeat(250)}@x.io`);
    ctx.click('send-code');
    await ctx.update();
    backend.expectNone('/api/v1/auth/recover');
    expect(ctx.el.textContent).toContain('أطول من 254 حرفًا');
  });
});
