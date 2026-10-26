import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { API_BASE_URL } from '../../core/api';
import type { StaffProfile } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { API } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { ProfileComponent } from './profile.component';

const profile: StaffProfile = {
  displayNameAr: 'أ. سارة م.', displayNameEn: 'Sara M.', titleAr: 'أخصائية', titleEn: 'Psychologist', bioAr: 'نبذة', bioEn: 'Bio',
  specialties: ['anxiety', 'sleep'], languages: ['ar', 'en'], gender: 'female', sessionType: 'remote', sessionMinutes: 50,
  capacity: 20, currentPatients: 14, acceptingNewPatients: true, licenseVerified: true, specialtyCatalog: ['anxiety', 'cbt', 'sleep'],
};

describe('ProfileComponent (S21)', () => {
  let backend: HttpTestingController;

  async function render(language: 'ar' | 'en' = 'en') {
    TestBed.configureTestingModule({
      imports: [ProfileComponent],
      providers: [provideTestTranslations(), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), { provide: API_BASE_URL, useValue: API }],
    });
    await TestBed.inject(LanguageService).use(language);
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProfileComponent);
    fixture.detectChanges();
    backend.expectOne(`${API}/v1/staff/profile`).flush(profile);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => backend.verify());

  function setValue(el: HTMLElement, testId: string, value: string) {
    const input = el.querySelector(`[data-testid="${testId}"]`) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('fills both languages and the current caseload, and refuses a capacity below it without calling the server', async () => {
    const { fixture, el } = await render();
    expect((el.querySelector('[data-testid="name-ar"]') as HTMLInputElement).value).toBe('أ. سارة م.');
    expect((el.querySelector('[data-testid="name-ar"]') as HTMLInputElement).getAttribute('dir')).toBe('rtl');
    expect(el.textContent).toContain('You have 14 patients now');
    expect(el.querySelector('[data-testid="license-status"]')!.textContent).toContain('Verified');
    setValue(el, 'capacity', '10');
    (el.querySelector('[data-testid="save"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="capacity-error"]')!.textContent).toContain('can’t be lower');
    backend.expectNone(`${API}/v1/staff/profile`);
  });

  it('saves the profile with the chosen specialties and shows the server error in Arabic', async () => {
    const { fixture, el } = await render('ar');
    (el.querySelector('[data-testid="specialty-cbt"] input') as HTMLInputElement).click();
    setValue(el, 'bio-en', 'Call 0551234567');
    (el.querySelector('[data-testid="save"]') as HTMLButtonElement).click();
    const req = backend.expectOne(`${API}/v1/staff/profile`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.specialties).toEqual(['anxiety', 'sleep', 'cbt']);
    expect(req.request.body.bioEn).toBe('Call 0551234567');
    req.flush({ title: 'احذف أرقام التواصل', status: 400, code: 'error.profile.contact_details', errors: { bioEn: ['contact_details'] } }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="problem"]')!.textContent).toContain('احذف أرقام التواصل');
  });
});
