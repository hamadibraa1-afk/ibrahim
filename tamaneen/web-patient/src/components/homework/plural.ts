/** Translation sub-keys for counted phrases. Arabic needs one, two and few (3–10); zero and many read as other. */
export type PluralKey = 'one' | 'two' | 'few' | 'other';

export function pluralKey(locale: string, n: number): PluralKey {
  const category = new Intl.PluralRules(locale).select(n);
  return category === 'one' || category === 'two' || category === 'few' ? category : 'other';
}
