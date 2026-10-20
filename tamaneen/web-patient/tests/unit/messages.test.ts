import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { namespaces } from '@/i18n/namespaces';

type Tree = { [key: string]: string | Tree };
const root = join(__dirname, '../../messages');

function load(lang: string): Tree {
  return Object.assign({}, ...readdirSync(join(root, lang)).map((f) => JSON.parse(readFileSync(join(root, lang, f), 'utf8')) as Tree));
}

function keys(tree: Tree, prefix = ''): string[] {
  return Object.entries(tree).flatMap(([k, v]) => (typeof v === 'string' ? [`${prefix}${k}`] : keys(v, `${prefix}${k}.`)));
}

function value(tree: Tree, key: string): string {
  return key.split('.').reduce<string | Tree>((n, p) => (n as Tree)[p]!, tree) as string;
}

function placeholders(text: string): string[] {
  return [...text.matchAll(/\{(\w+)[,}]/g)].map((m) => m[1]!).sort();
}

const ar = load('ar');
const en = load('en');

describe('translation files', () => {
  it('exist in both languages for exactly the registered namespaces, one top-level key each', () => {
    for (const lang of ['ar', 'en']) {
      const files = readdirSync(join(root, lang)).map((f) => f.replace(/\.json$/, '')).sort();
      expect(files).toEqual([...namespaces].filter((ns) => files.includes(ns)).sort());
      for (const f of files) {
        expect(Object.keys(JSON.parse(readFileSync(join(root, lang, `${f}.json`), 'utf8')))).toEqual([f]);
      }
    }
    expect(readdirSync(join(root, 'ar')).sort()).toEqual(readdirSync(join(root, 'en')).sort());
  });

  it('have exactly the same keys in Arabic and English', () => {
    expect(keys(ar).sort()).toEqual(keys(en).sort());
  });

  it('use the same placeholders in both languages', () => {
    for (const key of keys(ar)) {
      expect(placeholders(value(ar, key)), key).toEqual(placeholders(value(en, key)));
    }
  });

  it('have no empty strings', () => {
    for (const tree of [ar, en]) {
      for (const key of keys(tree)) {
        expect(value(tree, key).trim(), key).not.toBe('');
      }
    }
  });

  it('write Arabic text in Arabic', () => {
    // Allowed Latin: brand-neutral tokens such as PHQ-9, C-SSRS, CBT and the English language label.
    for (const key of keys(ar)) {
      const text = value(ar, key).replace(/\{[^}]*\}/g, '').replace(/PHQ-9|GAD-7|C-SSRS|CBT|English|P-XXXX-XXXX|ICS|PDF/g, '');
      const latin = (text.match(/[A-Za-z]/g) ?? []).length;
      expect(latin, key).toBeLessThanOrEqual(2);
    }
  });

  it('label the language switch in the target language', () => {
    expect((ar.languageToggle as Tree).target).toBe('English');
    expect((en.languageToggle as Tree).target).toBe('العربية');
  });
});
