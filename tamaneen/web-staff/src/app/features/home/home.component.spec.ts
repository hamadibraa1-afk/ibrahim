import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../core/api';
import { acceptLanguageInterceptor } from '../../core/i18n/accept-language.interceptor';
import { LanguageService } from '../../core/i18n/language.service';
import { SessionService } from '../../core/session/session.service';
import { provideTestTranslations } from '../../testing/translate-testing';
import { HomeComponent } from './home.component';

describe('HomeComponent', () => {
  it('asks the API again in the new language when the language changes', async () => {
    TestBed.configureTestingModule({
      imports: [HomeComponent, provideTestTranslations()],
      providers: [provideHttpClient(withInterceptors([acceptLanguageInterceptor])), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: '/api' }],
    });
    TestBed.inject(SessionService).setRoles(['therapist']);
    const language = TestBed.inject(LanguageService);
    await language.use('ar');
    const backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(HomeComponent);
    fixture.detectChanges();

    const first = backend.expectOne('/api/v1/meta/locale');
    expect(first.request.headers.get('Accept-Language')).toBe('ar');
    first.flush({ language: 'ar', dir: 'rtl', message: 'مرحبًا بك في طمأنينة.', supported: [] });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="api-message"]')!.textContent).toContain('مرحبًا');

    await language.use('en');
    fixture.detectChanges();
    const second = backend.expectOne('/api/v1/meta/locale');
    expect(second.request.headers.get('Accept-Language')).toBe('en');
    second.flush({ language: 'en', dir: 'ltr', message: 'Welcome to Tamaneen.', supported: [] });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Welcome to Tamaneen.');
    backend.verify();
    localStorage.clear();
  });

  it('shows a calm error with the crisis fallback when the API is down', async () => {
    TestBed.configureTestingModule({
      imports: [HomeComponent, provideTestTranslations()],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: '/api' }],
    });
    await TestBed.inject(LanguageService).use('en');
    const fixture = TestBed.createComponent(HomeComponent);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/meta/locale').error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')!.textContent).toContain('Crisis alerts still arrive');
    localStorage.clear();
  });
});
