import { formatCountdown, formatDate } from './format';

describe('formatCountdown', () => {
  it('shows minutes and seconds with two digits', () => {
    expect(formatCountdown(84_000)).toBe('01:24');
    expect(formatCountdown(600_000)).toBe('10:00');
    expect(formatCountdown(5_000)).toBe('00:05');
  });

  it('rounds partial seconds up so 00:00 means the deadline passed', () => {
    expect(formatCountdown(59_001)).toBe('01:00');
    expect(formatCountdown(1)).toBe('00:01');
    expect(formatCountdown(0)).toBe('00:00');
    expect(formatCountdown(-30_000)).toBe('00:00');
  });

  it('keeps counting minutes past an hour', () => {
    expect(formatCountdown(75 * 60_000)).toBe('75:00');
  });
});

describe('formatDate', () => {
  it('writes the long date with Gregorian months and Latin digits in Arabic', () => {
    expect(formatDate('2026-10-19', 'longDate', 'ar')).toBe('الاثنين، 19 أكتوبر 2026');
    expect(formatDate('2026-10-19', 'longDate', 'en')).toContain('19 October 2026');
  });

  it('returns nothing for missing or broken values', () => {
    expect(formatDate(null, 'time', 'en')).toBe('');
    expect(formatDate('not a date', 'time', 'en')).toBe('');
  });
});
