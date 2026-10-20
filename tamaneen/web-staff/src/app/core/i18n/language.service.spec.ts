import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';
import { provideTestTranslations } from '../../testing/translate-testing';
import { LANGUAGE_STORAGE_KEY } from './language';
import { LanguageService } from './language.service';

describe('LanguageService', () => {
  let doc: Document;

  function setup(): LanguageService {
    TestBed.configureTestingModule({ imports: [provideTestTranslations()] });
    doc = TestBed.inject(DOCUMENT);
    return TestBed.inject(LanguageService);
  }

  afterEach(() => {
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
  });

  it('starts from the saved choice', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'en');
    const service = setup();
    await service.init();
    expect(service.language()).toBe('en');
    expect(doc.documentElement.dir).toBe('ltr');
    expect(doc.documentElement.lang).toBe('en');
  });

  it('ignores a corrupted saved value', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'klingon');
    spyOnProperty(navigator, 'language').and.returnValue('ar-SA');
    const service = setup();
    await service.init();
    expect(service.language()).toBe('ar');
  });

  it('falls back to the browser language, then Arabic', async () => {
    spyOnProperty(navigator, 'language').and.returnValue('fr-FR');
    const service = setup();
    await service.init();
    expect(service.language()).toBe('ar');
    expect(service.direction()).toBe('rtl');
  });

  it('switches translations, direction and storage together', async () => {
    const service = setup();
    await service.init();
    await service.use('ar');
    const translate = TestBed.inject(TranslateService);
    expect(translate.instant('nav.patients')).toBe('مرضاي');

    await service.toggle();
    expect(service.language()).toBe('en');
    expect(service.direction()).toBe('ltr');
    expect(service.other()).toBe('ar');
    expect(translate.instant('nav.patients')).toBe('My patients');
    expect(doc.documentElement.dir).toBe('ltr');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en');
  });
});
