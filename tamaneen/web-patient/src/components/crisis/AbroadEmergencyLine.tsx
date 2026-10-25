import { useTranslations } from 'next-intl';

/** International emergency numbers that work outside the pilot region in most countries (EC-37). */
export const INTERNATIONAL_EMERGENCY_NUMBERS = ['112', '911'] as const;

function callChunk(number: string) {
  return function CallChunk() {
    return (
      <a href={`tel:${number}`} className="font-bold text-help underline underline-offset-4">
        <bdi dir="ltr">{number}</bdi>
      </a>
    );
  };
}

/**
 * "Outside the pilot region? Call 112 or 911, or the local emergency number" (EC-37). Shown on the crisis
 * screen (P06) and in the Help now panel. Both numbers are plain tel: links, so they work offline.
 */
export function AbroadEmergencyLine() {
  const t = useTranslations('crisis');
  const [first, second] = INTERNATIONAL_EMERGENCY_NUMBERS;
  return (
    <p data-testid="abroad-emergency" className="text-sm leading-relaxed text-muted">
      {t.rich('abroad', { first: callChunk(first), second: callChunk(second) })}
    </p>
  );
}
