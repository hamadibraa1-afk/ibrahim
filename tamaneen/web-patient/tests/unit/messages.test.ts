import ar from '../../messages/ar.json';
import en from '../../messages/en.json';

type Tree = { [key: string]: string | Tree };

function keys(tree: Tree, prefix = ''): string[] {
  return Object.entries(tree).flatMap(([k, v]) => (typeof v === 'string' ? [`${prefix}${k}`] : keys(v, `${prefix}${k}.`)));
}

function placeholders(text: string): string[] {
  return [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]!).sort();
}

describe('translation files', () => {
  it('have exactly the same keys in Arabic and English', () => {
    expect(keys(ar as Tree).sort()).toEqual(keys(en as Tree).sort());
  });

  it('use the same placeholders in both languages', () => {
    const flat = (tree: Tree) => Object.fromEntries(keys(tree).map((k) => [k, k.split('.').reduce<string | Tree>((n, p) => (n as Tree)[p]!, tree) as string]));
    const a = flat(ar as Tree);
    const e = flat(en as Tree);
    for (const key of Object.keys(a)) {
      expect(placeholders(a[key]!), key).toEqual(placeholders(e[key]!));
    }
  });

  it('have no empty strings', () => {
    for (const tree of [ar, en] as Tree[]) {
      for (const key of keys(tree)) {
        const value = key.split('.').reduce<string | Tree>((n, p) => (n as Tree)[p]!, tree);
        expect(String(value).trim(), key).not.toBe('');
      }
    }
  });

  it('label the language switch in the target language', () => {
    expect(ar.languageToggle.target).toBe('English');
    expect(en.languageToggle.target).toBe('العربية');
  });
});
