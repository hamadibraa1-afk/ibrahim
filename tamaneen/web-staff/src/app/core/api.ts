import { InjectionToken } from '@angular/core';
import { environment } from '../../environments/environment';

/** Tamaneen.Api. */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => environment.apiBaseUrl,
});

/** Tamaneen.Crisis.Worker: crisis events REST API and the /hubs/crisis SignalR hub. */
export const CRISIS_BASE_URL = new InjectionToken<string>('CRISIS_BASE_URL', {
  providedIn: 'root',
  factory: () => environment.crisisBaseUrl,
});

export interface LocaleResponse {
  language: string;
  dir: 'rtl' | 'ltr';
  message: string;
  supported: { code: string; name: string; dir: string }[];
}

/** True when the URL is the base itself or a path below it. "https://api.x" does not match "https://api.x.evil". */
export function isUnderBase(url: string, base: string): boolean {
  if (!base) {
    return false;
  }
  const trimmed = base.endsWith('/') ? base.slice(0, -1) : base;
  return url === trimmed || url.startsWith(`${trimmed}/`) || url.startsWith(`${trimmed}?`);
}
