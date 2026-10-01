import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ChangePasswordRequest, LoginRequest, LoginResponse, SessionResponse, User, UserRole,
} from '../models/user.model';

/**
 * لا يُحفظ رمز الجلسة في المتصفح إطلاقاً: الخادم يضعه في كعكة HttpOnly + Secure + SameSite=Strict
 * لا يمكن لأي سكربت قراءتها. نحفظ هنا بيانات العرض فقط (الملف الشخصي وموعد الانتهاء).
 */
export const USER_KEY = 'sci.proposals.user';
const EXPIRY_KEY = 'sci.proposals.expiresAt';
/** مفاتيح النسخة السابقة التي كانت تحفظ الرمز — تُمسح عند الإقلاع. */
const LEGACY_TOKEN_KEY = 'sci.proposals.token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/auth`;

  private _user = signal<User | null>(this.readStoredUser());
  readonly currentUser = this._user.asReadonly();
  readonly isLoggedIn = computed(() => this._user() !== null);
  readonly role = computed<UserRole | null>(() => this._user()?.role ?? null);

  /** ثوانٍ متبقية على انتهاء الجلسة — يحدّثها SessionManager. */
  private _secondsLeft = signal<number>(0);
  readonly secondsLeft = this._secondsLeft.asReadonly();

  constructor() {
    localStorage.removeItem(LEGACY_TOKEN_KEY);
    // جلسة محفوظة لكنها منتهية → نظّفها فوراً عند إقلاع التطبيق
    if (this._user() && this.isExpired()) this.clear();
    this.recomputeSecondsLeft();
  }

  get expiresAt(): Date | null {
    const raw = localStorage.getItem(EXPIRY_KEY);
    return raw ? new Date(raw) : null;
  }

  isExpired(): boolean {
    const exp = this.expiresAt;
    return !exp || exp.getTime() <= Date.now();
  }

  recomputeSecondsLeft(): number {
    const exp = this.expiresAt;
    const left = exp ? Math.max(0, Math.floor((exp.getTime() - Date.now()) / 1000)) : 0;
    this._secondsLeft.set(left);
    return left;
  }

  /** الخادم يمدّد الجلسة مع كل طلب (8 ساعات منزلقة) ويعيد الموعد الجديد في ترويسة. */
  syncExpiry(expiresAt: string): void {
    if (!this.isLoggedIn()) return;
    const iso = new Date(expiresAt).toISOString();
    if (localStorage.getItem(EXPIRY_KEY) === iso) return;
    localStorage.setItem(EXPIRY_KEY, iso);
    this.recomputeSecondsLeft();
  }

  login(payload: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.base}/login`, payload).pipe(
      tap(res => this.persist(res.expiresAt, res.user))
    );
  }

  /**
   * الخروج يمسح الحالة **فوراً وبشكل متزامن** قبل أي انتظار للشبكة،
   * حتى يعمل التوجيه لصفحة الدخول من أول محاولة دون الحاجة لتحديث الصفحة.
   * إبطال الجلسة على الخادم وحذف الكعكة يجريان بعدها ولا يؤخّران المستخدم.
   */
  logout(): void {
    const wasLoggedIn = this.isLoggedIn();
    this.clear();
    if (wasLoggedIn) {
      this.http.post(`${this.base}/logout`, {}).subscribe({ next: () => {}, error: () => {} });
    }
  }

  /** يمدّد الجلسة صراحةً (زر "متابعة العمل"). */
  refreshSession(): Observable<SessionResponse> {
    return this.http.post<SessionResponse>(`${this.base}/refresh`, {}).pipe(
      tap(res => this.syncExpiry(res.expiresAt))
    );
  }

  refreshMe(): Observable<User> {
    return this.http.get<User>(`${this.base}/me`).pipe(
      tap(user => {
        this._user.set(user);
        localStorage.setItem(USER_KEY, JSON.stringify(user));
      })
    );
  }

  changePassword(payload: ChangePasswordRequest): Observable<SessionResponse> {
    // الخادم يبطل الجلسات الأخرى ويبقي هذه الجلسة — نحدّث موعد انتهائها فقط
    return this.http.post<SessionResponse>(`${this.base}/change-password`, payload).pipe(
      tap(res => this.syncExpiry(res.expiresAt))
    );
  }

  clear(): void {
    localStorage.removeItem(USER_KEY);
    localStorage.removeItem(EXPIRY_KEY);
    this._user.set(null);
    this._secondsLeft.set(0);
  }

  private persist(expiresAt: string | Date, user: User): void {
    localStorage.setItem(EXPIRY_KEY, new Date(expiresAt).toISOString());
    localStorage.setItem(USER_KEY, JSON.stringify(user));
    this._user.set(user);
    this.recomputeSecondsLeft();
  }

  private readStoredUser(): User | null {
    const raw = localStorage.getItem(USER_KEY);
    if (!raw) return null;
    try { return JSON.parse(raw) as User; } catch { return null; }
  }
}
