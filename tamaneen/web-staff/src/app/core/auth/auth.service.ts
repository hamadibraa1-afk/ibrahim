import { HttpClient, HttpContext, HttpContextToken } from '@angular/common/http';
import { Injectable, NgZone, OnDestroy, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../api';
import type { StaffLoginRequest, StaffLoginResponse, StaffMe, StaffMfaRequest, StaffMfaResponse, StaffSession } from '../api/types';
import { SessionService } from '../session/session.service';

/** Marks requests the auth interceptor must not retry after refreshing (the auth calls themselves). */
export const SKIP_AUTH_RETRY = new HttpContextToken<boolean>(() => false);

/** The access token is refreshed this long before it expires. */
export const REFRESH_LEAD_MS = 60_000;

export type AuthStatus = 'signedOut' | 'signedIn';

/**
 * Staff sign-in (S06): password, then a mandatory authenticator (or recovery) code.
 * The access token lives in memory only. The refresh token is an HttpOnly cookie set by Tamaneen.Api,
 * so auth calls go with credentials and the refresh body is empty.
 */
@Injectable({ providedIn: 'root' })
export class AuthService implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(API_BASE_URL);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly zone = inject(NgZone);

  private readonly token = signal<string | null>(null);
  private readonly profile = signal<StaffMe | null>(null);
  private expiresAt = 0;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;
  private inFlightRefresh: Promise<string | null> | null = null;

  readonly me = this.profile.asReadonly();
  readonly status = computed<AuthStatus>(() => (this.token() && this.profile() ? 'signedIn' : 'signedOut'));
  readonly signedIn = computed(() => this.status() === 'signedIn');

  /** Current access token for the Authorization header, or null. */
  accessToken(): string | null {
    return this.token();
  }

  /** A token that is still good for a while, refreshing first when it is about to expire (SignalR negotiate/reconnect). */
  async validAccessToken(): Promise<string> {
    const token = this.token();
    if (token && this.expiresAt - Date.now() > 30_000) {
      return token;
    }
    return (await this.refresh()) ?? '';
  }

  /** Step 1: contact and password. Resolves with the MFA challenge; rejects with the HTTP error (401 remainingAttempts, 423 retryAfterSeconds). */
  login(contact: string, password: string): Promise<StaffLoginResponse> {
    const body: StaffLoginRequest = { contact, password, client: 'staff' };
    return firstValueFrom(this.http.post<StaffLoginResponse>(`${this.apiBase}/v1/auth/login`, body, this.authOptions()));
  }

  /** Step 2: authenticator or recovery code. On success the session is live and the profile loaded. */
  async completeMfa(mfaToken: string, method: 'totp' | 'recovery', code: string): Promise<StaffMe> {
    const body: StaffMfaRequest = { mfaToken, method, code: code.trim(), rememberDevice: false };
    const { tokens } = await firstValueFrom(this.http.post<StaffMfaResponse>(`${this.apiBase}/v1/auth/login/mfa`, body, this.authOptions()));
    this.acceptSession(tokens);
    return this.loadProfile();
  }

  /** On app start: a silent refresh from the cookie. Resolves true when a session was restored. */
  async restore(): Promise<boolean> {
    const token = await this.refresh();
    if (!token) {
      return false;
    }
    try {
      await this.loadProfile();
      return true;
    } catch {
      this.clearLocal();
      return false;
    }
  }

  /** Exchanges the refresh cookie for a new access token. Concurrent callers share one request. Resolves null when the session is over. */
  refresh(): Promise<string | null> {
    if (!this.inFlightRefresh) {
      this.inFlightRefresh = firstValueFrom(this.http.post<StaffSession>(`${this.apiBase}/v1/auth/refresh`, {}, this.authOptions()))
        .then((tokens) => {
          this.acceptSession(tokens);
          return tokens.accessToken;
        })
        .catch(() => {
          this.clearLocal();
          return null;
        })
        .finally(() => {
          this.inFlightRefresh = null;
        });
    }
    return this.inFlightRefresh;
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>(`${this.apiBase}/v1/auth/logout`, {}, this.authOptions()));
    } catch {
      // The local session ends regardless; the cookie expires on its own if the server could not be reached.
    }
    this.clearLocal();
    await this.router.navigateByUrl('/login');
  }

  /** The session ended on the server (refresh refused): clear it and go to sign-in, coming back here afterwards. */
  expire(): void {
    this.clearLocal();
    const url = this.router.url;
    if (url.startsWith('/login')) {
      return;
    }
    void this.router.navigate(['/login'], { queryParams: url && url !== '/' ? { returnUrl: url } : {} });
  }

  ngOnDestroy(): void {
    clearTimeout(this.refreshTimer);
  }

  private async loadProfile(): Promise<StaffMe> {
    const me = await firstValueFrom(this.http.get<StaffMe>(`${this.apiBase}/v1/staff/me`));
    this.session.setRoles([me.role]);
    this.session.setProfile({ displayName: me.displayName, onCall: me.onCall });
    this.profile.set(me);
    return me;
  }

  private acceptSession(tokens: StaffSession): void {
    this.token.set(tokens.accessToken);
    this.expiresAt = new Date(tokens.accessExpiresAt).getTime();
    this.scheduleRefresh();
  }

  private scheduleRefresh(): void {
    clearTimeout(this.refreshTimer);
    const delay = Math.max(0, this.expiresAt - Date.now() - REFRESH_LEAD_MS);
    // Outside the zone: a timer pending for minutes would keep the app from ever being "stable".
    this.refreshTimer = this.zone.runOutsideAngular(() =>
      setTimeout(() => {
        this.zone.run(() => {
          void this.refresh().then((token) => {
            if (!token) {
              this.expire();
            }
          });
        });
      }, delay),
    );
  }

  private clearLocal(): void {
    clearTimeout(this.refreshTimer);
    this.refreshTimer = undefined;
    this.token.set(null);
    this.profile.set(null);
    this.expiresAt = 0;
    this.session.clear();
  }

  private authOptions() {
    return { withCredentials: true, context: new HttpContext().set(SKIP_AUTH_RETRY, true) };
  }
}
