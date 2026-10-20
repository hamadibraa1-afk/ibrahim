import type { MoodTag } from '@/lib/api/types';

export type MoodWord = 'veryBad' | 'bad' | 'okay' | 'good' | 'great';

/** The five faces on the home screen and the score each one saves (the scale is 0–10). */
export const MOOD_FACES: readonly { key: MoodWord; score: number }[] = [
  { key: 'veryBad', score: 0 },
  { key: 'bad', score: 3 },
  { key: 'okay', score: 5 },
  { key: 'good', score: 7 },
  { key: 'great', score: 10 },
];

export const MOOD_TAGS: readonly MoodTag[] = ['tired', 'anxious', 'slept_well', 'work_stress', 'calm'];

/** The word that describes a score: 0–1 very bad, 2–3 bad, 4–6 okay, 7–8 good, 9–10 excellent. */
export function moodWord(score: number): MoodWord {
  if (score <= 1) return 'veryBad';
  if (score <= 3) return 'bad';
  if (score <= 6) return 'okay';
  if (score <= 8) return 'good';
  return 'great';
}

/** The face closest to a saved score, so today's entry shows as selected. */
export function nearestFace(score: number): MoodWord {
  let best = MOOD_FACES[0]!;
  for (const face of MOOD_FACES) {
    if (Math.abs(face.score - score) < Math.abs(best.score - score)) best = face;
  }
  return best.key;
}
