/**
 * Runtime configuration. Loaded from /config.json before the app starts, so the same
 * build can point at a different API without rebuilding: edit config.json on the host.
 * Empty apiBase means "same origin" (development proxy, or API behind the same domain).
 */
export const AppConfig = { apiBase: '' };

export async function loadConfig(): Promise<void> {
  try {
    const res = await fetch('/config.json', { cache: 'no-store' });
    if (!res.ok) return;
    const cfg = (await res.json()) as Partial<typeof AppConfig>;
    AppConfig.apiBase = (cfg.apiBase ?? '').replace(/\/$/, '');
  } catch {
    // No config file (or offline): keep same-origin defaults.
  }
}
