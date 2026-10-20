import { directionOf, isLocale, otherLocale } from '@/i18n/routing';

describe('locale routing helpers', () => {
  it('maps Arabic to RTL and English to LTR', () => {
    expect(directionOf('ar')).toBe('rtl');
    expect(directionOf('en')).toBe('ltr');
  });

  it('toggles between the two languages', () => {
    expect(otherLocale('ar')).toBe('en');
    expect(otherLocale('en')).toBe('ar');
  });

  it('accepts only ar and en', () => {
    expect(isLocale('ar')).toBe(true);
    expect(isLocale('en')).toBe(true);
    expect(isLocale('fr')).toBe(false);
    expect(isLocale('AR')).toBe(false);
    expect(isLocale(undefined)).toBe(false);
  });
});
