/** Server always returns UAE time (+04:00), so we slice the ISO string instead of converting to the browser zone. */
export const hm = (iso?: string | null): string => (iso ? iso.substring(11, 16) : '—');
export const ymd = (iso?: string | null): string => (iso ? iso.substring(0, 10) : '—');
export const hmin = (minutes: number | null | undefined): string => {
  if (!minutes) return '0:00';
  const m = Math.abs(minutes);
  return `${minutes < 0 ? '-' : ''}${Math.floor(m / 60)}:${String(m % 60).padStart(2, '0')}`;
};
/** 'YYYY-MM-DD' for the UAE calendar day. */
export const uaeToday = (): string => new Date(Date.now() + 4 * 3600_000).toISOString().substring(0, 10);
export const addDays = (d: string, n: number): string => { const x = new Date(d + 'T00:00:00Z'); x.setUTCDate(x.getUTCDate() + n); return x.toISOString().substring(0, 10); };
export const weekday = (d: string): number => new Date(d + 'T00:00:00Z').getUTCDay();
export const weekStart = (d: string): string => addDays(d, -weekday(d));
/** API TimeOnly expects HH:mm:ss. */
export const toTimeOnly = (v: string | null | undefined): string | null => (v ? (v.length === 5 ? v + ':00' : v) : null);
export const timeInput = (v: string | null | undefined): string => (v ? v.substring(0, 5) : '');
export const nowUaeHm = (): string => new Date(Date.now() + 4 * 3600_000).toISOString().substring(11, 16);

export function downloadCsv(filename: string, rows: (string | number | null | undefined)[][]): void {
  const esc = (v: any) => { const s = v === null || v === undefined ? '' : String(v); return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s; };
  const csv = '\uFEFF' + rows.map(r => r.map(esc).join(',')).join('\r\n'); // BOM so Excel reads Arabic correctly
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
  a.download = filename;
  a.click();
  URL.revokeObjectURL(a.href);
}

export function getPosition(): Promise<GeolocationPosition> {
  return new Promise((resolve, reject) => {
    if (!navigator.geolocation) { reject(new Error('unsupported')); return; }
    navigator.geolocation.getCurrentPosition(resolve, reject, { enableHighAccuracy: true, timeout: 20000, maximumAge: 0 });
  });
}
