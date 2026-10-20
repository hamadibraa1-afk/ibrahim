import type { Locale } from '@/i18n/routing';
import { formatRelativeDays } from '@/lib/format';
import type { Gender, Lang, MatchSuggestion } from '@/lib/api/types';
import { genderKey, joinList, specialtyLabels, type Translator } from './labels';

export interface ReasonInput {
  reasons: MatchSuggestion['reasons'];
  gender: Gender;
  /** The patient's own language (Me.language): "speaks Arabic". */
  patientLanguage: Lang;
  locale: Locale;
  timeZone: string;
  now?: Date;
}

/**
 * "خبرة في الضغط المهني ومشكلات النوم، وتتحدث العربية، وأقرب موعد بعد يومين."
 * Built from the structured reasons the API returns, so no free text from the server is needed.
 * `t` is the "matching" translator, `tSpecialties` the "specialties" one. Returns null when there is nothing to say.
 */
export function reasonSentence(input: ReasonInput, t: Translator, tSpecialties: Translator): string | null {
  const { reasons, gender, patientLanguage, locale, timeZone, now = new Date() } = input;
  const clauses: string[] = [];

  // Mid-sentence in English, "Work stress" reads as "work stress"; acronyms such as CBT stay as they are.
  const shared = specialtyLabels(tSpecialties, reasons.sharedSpecialties).map((label) =>
    locale === 'en' && /^[A-Z][a-z]/.test(label) ? label.charAt(0).toLowerCase() + label.slice(1) : label,
  );
  if (shared.length > 0) clauses.push(t('reasons.specialties', { list: joinList(shared, locale) }));
  if (reasons.speaksYourLanguage) clauses.push(t(genderKey('reasons.language', gender), { language: t(`reasons.languageName.${patientLanguage}`) }));
  if (reasons.eveningSlots) clauses.push(t('reasons.evening'));
  if (reasons.nextAvailableAt) clauses.push(t('reasons.next', { when: formatRelativeDays(reasons.nextAvailableAt, locale, now, timeZone) }));

  if (clauses.length === 0) return null;
  const joined = clauses.reduce((text, clause, i) => {
    if (i === 0) return clause;
    const sep = clauses.length === 2 ? t('reasons.joinPair') : i === clauses.length - 1 ? t('reasons.joinLast') : t('reasons.join');
    return `${text}${sep}${clause}`;
  });
  const sentence = t('reasons.sentence', { reasons: joined });
  return sentence.charAt(0).toLocaleUpperCase(locale) + sentence.slice(1);
}
