import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { API_BASE_URL } from '../../core/api';
import { LanguageService } from '../../core/i18n/language.service';
import { SessionService } from '../../core/session/session.service';
import { API } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { ForbiddenComponent, refusedPath } from './forbidden.component';

describe('ForbiddenComponent', () => {
  let backend: HttpTestingController;

  async function render(from: string | null) {
    TestBed.configureTestingModule({
      imports: [ForbiddenComponent],
      providers: [
        provideTestTranslations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: API },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(from ? { from } : {}) } } },
      ],
    });
    await TestBed.inject(LanguageService).use('en');
    TestBed.inject(SessionService).setRoles(['supervisor']);
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ForbiddenComponent);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => {
    backend.verify();
    localStorage.clear();
  });

  it('says the page is not allowed, records the attempt and links back to the role home', async () => {
    const { fixture, el } = await render('/dashboard');
    const report = backend.expectOne(`${API}/v1/staff/access-denied`);
    expect(report.request.body).toEqual({ path: '/dashboard' });
    report.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    expect(el.querySelector('h1')!.textContent).toContain("You don't have access to this page");
    expect(el.querySelector('[data-testid="forbidden-logged"]')!.textContent).toContain('recorded in the audit log');
    const back = el.querySelector('[data-testid="back-home"]')!;
    expect(back.textContent).toContain('Back to my dashboard');
    expect(back.getAttribute('href')).toBe('/crisis');
  });

  it('does not claim the attempt was recorded when the report fails', async () => {
    const { fixture, el } = await render('/crisis');
    backend.expectOne(`${API}/v1/staff/access-denied`).flush({ title: 'x', status: 500 }, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="forbidden-logged"]')).toBeNull();
    expect(el.querySelector('[data-testid="forbidden-log-failed"]')).not.toBeNull();
  });

  it('only reports portal paths', async () => {
    expect(refusedPath('//evil.example')).toBeNull();
    expect(refusedPath('https://x')).toBeNull();
    expect(refusedPath(null)).toBeNull();
    expect(refusedPath('/crisis/ev-1')).toBe('/crisis/ev-1');
    await render(null);
    backend.expectNone(`${API}/v1/staff/access-denied`);
  });
});
