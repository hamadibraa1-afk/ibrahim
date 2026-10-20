import type { MoodWord } from './scale';

// Colours and mouths from the design file, from very bad (warm) to excellent (teal).
const faces: Record<MoodWord, { fill: string; ink: string; mouth: string }> = {
  veryBad: { fill: '#F6C9B8', ink: '#5A2E22', mouth: 'M10 23c3-4 9-4 12 0' },
  bad: { fill: '#FBD9B4', ink: '#5A3A1E', mouth: 'M11 22c3-2 7-2 10 0' },
  okay: { fill: '#FBE7B0', ink: '#5A4A1E', mouth: 'M11 21h10' },
  good: { fill: '#CFE8C9', ink: '#24452A', mouth: 'M11 20c3 2 7 2 10 0' },
  great: { fill: '#B9E0D6', ink: '#163F3C', mouth: 'M10 19c3 5 9 5 12 0' },
};

export function FaceIcon({ mood, size = 32 }: { mood: MoodWord; size?: number }) {
  const f = faces[mood];
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true">
      <circle cx="16" cy="16" r="14" fill={f.fill} />
      <circle cx="11" cy="13" r="1.8" fill={f.ink} />
      <circle cx="21" cy="13" r="1.8" fill={f.ink} />
      <path d={f.mouth} stroke={f.ink} strokeWidth="2" fill="none" strokeLinecap="round" />
    </svg>
  );
}
