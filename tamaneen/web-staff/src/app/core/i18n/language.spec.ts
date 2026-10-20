import { directionOf, isLanguage, otherLanguage, toLanguage } from './language';
import { testTranslations } from '../../testing/translate-testing';

describe('language helpers', () => {
  it('maps Arabic to RTL and English to LTR', () => {
    expect(directionOf('ar')).toBe('rtl');
    expect(directionOf('en')).toBe('ltr');
  });

  it('normalizes regional tags and rejects others', () => {
    expect(toLanguage('ar-SA')).toBe('ar');
    expect(toLanguage('en_GB')).toBe('en');
    expect(toLanguage('EN')).toBe('en');
    expect(toLanguage('fr-FR')).toBeNull();
    expect(toLanguage(undefined)).toBeNull();
    expect(isLanguage('de')).toBeFalse();
  });

  it('toggles between the two languages', () => {
    expect(otherLanguage('ar')).toBe('en');
    expect(otherLanguage('en')).toBe('ar');
  });
});

describe('translation files', () => {
  type Tree = { [key: string]: string | Tree };
  const keys = (tree: Tree, prefix = ''): string[] =>
    Object.entries(tree).flatMap(([k, v]) => (typeof v === 'string' ? [`${prefix}${k}`] : keys(v, `${prefix}${k}.`)));
  const value = (tree: Tree, key: string) => key.split('.').reduce<string | Tree>((n, p) => (n as Tree)[p], tree) as string;
  const params = (text: string) => [...text.matchAll(/\{\{\s*(\w+)\s*\}\}/g)].map((m) => m[1]).sort();

  it('have the same keys in Arabic and English', () => {
    expect(keys(testTranslations.ar as Tree).sort()).toEqual(keys(testTranslations.en as Tree).sort());
  });

  it('use the same parameters and no empty values', () => {
    for (const key of keys(testTranslations.ar as Tree)) {
      const ar = value(testTranslations.ar as Tree, key);
      const en = value(testTranslations.en as Tree, key);
      expect(params(ar)).withContext(key).toEqual(params(en));
      expect(ar.trim()).withContext(key).not.toBe('');
      expect(en.trim()).withContext(key).not.toBe('');
    }
  });

  it('cover every menu item of every role', async () => {
    const { NAVIGATION } = await import('../session/staff-role');
    for (const groups of Object.values(NAVIGATION)) {
      for (const group of groups) {
        expect(value(testTranslations.en as Tree, `navGroups.${group.key}`)).withContext(group.key).toBeTruthy();
        for (const item of group.items) {
          expect(value(testTranslations.ar as Tree, `nav.${item.key}`)).withContext(item.key).toBeTruthy();
          expect(value(testTranslations.en as Tree, `nav.${item.key}`)).withContext(item.key).toBeTruthy();
        }
      }
    }
  });
});
