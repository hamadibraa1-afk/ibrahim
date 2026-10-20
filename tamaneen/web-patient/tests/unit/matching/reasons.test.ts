import { createTranslator } from 'next-intl';
import type { Translator } from '@/components/matching/labels';
import { reasonSentence } from '@/components/matching/reasons';
import { formatRelativeDays } from '@/lib/format';
import { messagesFor } from '../intl';
import { NOW } from './fixtures';

function translators(locale: 'ar' | 'en') {
  const messages = messagesFor(locale);
  const t = createTranslator({ locale, messages, namespace: 'matching' }) as unknown as Translator;
  const ts = createTranslator({ locale, messages, namespace: 'specialties' }) as unknown as Translator;
  return { t, ts };
}

const nextAt = '2026-10-21T13:00:00Z';
const full = { sharedSpecialties: ['work_stress', 'sleep'], speaksYourLanguage: true, eveningSlots: false, nextAvailableAt: nextAt };

describe('reasonSentence', () => {
  it('writes the Arabic sentence from structured reasons, with the therapist’s grammatical gender', () => {
    const { t, ts } = translators('ar');
    const when = formatRelativeDays(nextAt, 'ar', NOW);
    expect(reasonSentence({ reasons: full, gender: 'female', patientLanguage: 'ar', locale: 'ar', timeZone: 'Asia/Riyadh', now: NOW }, t, ts)).toBe(
      `خبرة في الضغط المهني ومشكلات النوم، وتتحدث العربية، وأقرب موعد ${when}.`,
    );
    expect(
      reasonSentence({ reasons: { ...full, sharedSpecialties: [], nextAvailableAt: null }, gender: 'male', patientLanguage: 'ar', locale: 'ar', timeZone: 'Asia/Riyadh', now: NOW }, t, ts),
    ).toBe('يتحدث العربية.');
  });

  it('writes a natural English sentence with a serial "and"', () => {
    const { t, ts } = translators('en');
    expect(reasonSentence({ reasons: full, gender: 'female', patientLanguage: 'ar', locale: 'en', timeZone: 'Asia/Riyadh', now: NOW }, t, ts)).toBe(
      'Experienced in work stress and sleep problems, speaks Arabic, and the earliest slot is in 2 days.',
    );
    expect(
      reasonSentence(
        { reasons: { sharedSpecialties: ['anxiety'], speaksYourLanguage: false, eveningSlots: true, nextAvailableAt: null }, gender: 'male', patientLanguage: 'en', locale: 'en', timeZone: 'Asia/Riyadh', now: NOW },
        t,
        ts,
      ),
    ).toBe('Experienced in anxiety and has evening slots.');
  });

  it('pairs two Arabic clauses with "، و" and drops unknown specialty codes', () => {
    const { t, ts } = translators('ar');
    expect(
      reasonSentence(
        { reasons: { sharedSpecialties: ['anxiety', 'not_a_code'], speaksYourLanguage: false, eveningSlots: true, nextAvailableAt: null }, gender: 'male', patientLanguage: 'ar', locale: 'ar', timeZone: 'Asia/Riyadh', now: NOW },
        t,
        ts,
      ),
    ).toBe('خبرة في القلق، ومواعيد مسائية متاحة.');
  });

  it('returns null when there is no reason to give', () => {
    const { t, ts } = translators('en');
    expect(
      reasonSentence(
        { reasons: { sharedSpecialties: [], speaksYourLanguage: false, eveningSlots: false, nextAvailableAt: null }, gender: 'female', patientLanguage: 'en', locale: 'en', timeZone: 'Asia/Riyadh' },
        t,
        ts,
      ),
    ).toBeNull();
  });
});
