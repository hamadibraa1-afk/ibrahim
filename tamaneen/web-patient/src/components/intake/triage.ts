/** Whole hours until `iso`, rounded up; 0 once the time has passed. */
export function hoursUntil(iso: string, now = Date.now()): number {
  const target = Date.parse(iso);
  return Number.isNaN(target) ? 0 : Math.max(0, Math.ceil((target - now) / 3_600_000));
}
