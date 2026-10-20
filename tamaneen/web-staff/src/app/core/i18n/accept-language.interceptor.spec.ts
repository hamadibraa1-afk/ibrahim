import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTestTranslations } from '../../testing/translate-testing';
import { API_BASE_URL, CRISIS_BASE_URL } from '../api';
import { acceptLanguageInterceptor } from './accept-language.interceptor';
import { LanguageService } from './language.service';

describe('acceptLanguageInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let language: LanguageService;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideTestTranslations(),
        provideHttpClient(withInterceptors([acceptLanguageInterceptor])),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'https://api.tamaneen.test' },
        { provide: CRISIS_BASE_URL, useValue: 'https://crisis.tamaneen.test' },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    language = TestBed.inject(LanguageService);
    await language.use('ar');
  });

  afterEach(() => {
    backend.verify();
    localStorage.clear();
  });

  it('sends the current language to the API', async () => {
    http.get('https://api.tamaneen.test/v1/meta/locale').subscribe();
    expect(backend.expectOne('https://api.tamaneen.test/v1/meta/locale').request.headers.get('Accept-Language')).toBe('ar');

    await language.use('en');
    http.get('https://api.tamaneen.test/v1/me').subscribe();
    expect(backend.expectOne('https://api.tamaneen.test/v1/me').request.headers.get('Accept-Language')).toBe('en');
  });

  it('sends it to the crisis worker too, so event titles and labels are localized', () => {
    http.get('https://crisis.tamaneen.test/v1/crisis/events/open').subscribe();
    expect(backend.expectOne('https://crisis.tamaneen.test/v1/crisis/events/open').request.headers.get('Accept-Language')).toBe('ar');
  });

  it('does not touch requests to other origins', () => {
    http.get('https://api.tamaneen.test.evil.example/v1/x').subscribe();
    http.get('https://fonts.googleapis.com/css2').subscribe();
    expect(backend.expectOne('https://api.tamaneen.test.evil.example/v1/x').request.headers.has('Accept-Language')).toBeFalse();
    expect(backend.expectOne('https://fonts.googleapis.com/css2').request.headers.has('Accept-Language')).toBeFalse();
  });
});
