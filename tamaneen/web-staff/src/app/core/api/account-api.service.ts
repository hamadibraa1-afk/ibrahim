import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api';
import type {
  AlertStatusFilter,
  Alerts,
  Inbox,
  InboxFilter,
  Lang,
  StaffNotificationPrefs,
  StaffPasswordRequest,
  StaffProfile,
  StaffSecurity,
  StepUpRequest,
  TotpMove,
  UnreadCount,
  UpdateStaffProfileRequest,
} from './types';

/** Phase 5 staff endpoints: inbox (S41), alerts (S20), professional profile (S21), account settings (S32). */
@Injectable({ providedIn: 'root' })
export class AccountApiService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  inbox(filter: InboxFilter = 'all', cursor?: string | null): Observable<Inbox> {
    let params = new HttpParams().set('category', filter);
    if (cursor) params = params.set('cursor', cursor);
    return this.http.get<Inbox>(`${this.base}/v1/staff/inbox`, { params });
  }

  unreadCount(): Observable<UnreadCount> {
    return this.http.get<UnreadCount>(`${this.base}/v1/staff/inbox/unread-count`);
  }

  markRead(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/v1/staff/inbox/${encodeURIComponent(id)}/read`, {});
  }

  /** Unacknowledged risk alerts stay; they leave only when someone acknowledges the event. */
  markAllRead(): Observable<void> {
    return this.http.post<void>(`${this.base}/v1/staff/inbox/read-all`, {});
  }

  alerts(status: AlertStatusFilter): Observable<Alerts> {
    return this.http.get<Alerts>(`${this.base}/v1/staff/alerts`, { params: new HttpParams().set('status', status) });
  }

  profile(): Observable<StaffProfile> {
    return this.http.get<StaffProfile>(`${this.base}/v1/staff/profile`);
  }

  saveProfile(request: UpdateStaffProfileRequest): Observable<StaffProfile> {
    return this.http.put<StaffProfile>(`${this.base}/v1/staff/profile`, request);
  }

  security(): Observable<StaffSecurity> {
    return this.http.get<StaffSecurity>(`${this.base}/v1/staff/me/security`);
  }

  changePassword(request: StaffPasswordRequest): Observable<void> {
    return this.http.put<void>(`${this.base}/v1/staff/me/password`, request);
  }

  startTotpMove(request: StepUpRequest): Observable<TotpMove> {
    return this.http.post<TotpMove>(`${this.base}/v1/staff/me/totp/move`, request);
  }

  confirmTotpMove(moveToken: string, code: string): Observable<void> {
    return this.http.post<void>(`${this.base}/v1/staff/me/totp/move/confirm`, { moveToken, code });
  }

  recoveryCodes(request: StepUpRequest): Observable<{ recoveryCodes: string[] }> {
    return this.http.post<{ recoveryCodes: string[] }>(`${this.base}/v1/staff/me/recovery-codes`, request);
  }

  revokeSession(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/v1/staff/me/sessions/${encodeURIComponent(id)}`);
  }

  revokeOtherSessions(): Observable<void> {
    return this.http.post<void>(`${this.base}/v1/staff/me/sessions/revoke-others`, {});
  }

  notificationPrefs(): Observable<StaffNotificationPrefs> {
    return this.http.get<StaffNotificationPrefs>(`${this.base}/v1/staff/me/notifications`);
  }

  saveNotificationPrefs(prefs: StaffNotificationPrefs): Observable<StaffNotificationPrefs> {
    return this.http.put<StaffNotificationPrefs>(`${this.base}/v1/staff/me/notifications`, prefs);
  }

  /** The server writes notifications and messages in this language. */
  saveLanguage(language: Lang): Observable<unknown> {
    return this.http.put(`${this.base}/v1/me/language`, { language });
  }
}
