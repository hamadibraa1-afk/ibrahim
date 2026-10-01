import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ChangePasswordRequest, LoginRequest, LoginResponse, User, UserRole,
} from '../models/user.model';

const TOKEN_KEY = 'sci.proposals.token';
const USER_KEY = 'sci.proposals.user';
const EXPIRY_KEY = 'sci.proposals.expiresAt';

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
    // جلسة محفوظة لكنها منتهية → نظّفها فوراً عند إقلاع التطبيق
    if (this._user() && this.isExpired()) this.clear();
    this.recomputeSecondsLeft();
  }

  get token(): string | null { return localStorage.getItem(TOKEN_KEY); }

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

  login(payload: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.base}/login`, payload).pipe(
      tap(res => this.persist(res.token, res.expiresAt, res.user))
    );
  }

  /**
   * الخروج يمسح الحالة **فوراً وبشكل متزامن** قبل أي انتظار للشبكة،
   * حتى يعمل التوجيه لصفحة الدخول من أول محاولة دون الحاجة لتحديث الصفحة.
   * إبطال الجلسة على الخادم يجري بعدها ولا يؤخّر المستخدم.
   */
  logout(): void {
    const token = this.token;
    this.clear();

    if (token) {
      this.http.post(`${this.base}/logout`, {}, {
        headers: { Authorization: `Bearer ${token}` },
      }).subscribe({ next: () => {}, error: () => {} });
    }
  }

  /** يمدّد الجلسة عند نشاط المستخدم (جلسة منزلقة). */
  refreshSession(): Observable<{ token: string; expiresAt: string }> {
    return this.http.post<{ token: string; expiresAt: string }>(`${this.base}/refresh`, {}).pipe(
      tap(res => {
        localStorage.setItem(TOKEN_KEY, res.token);
        localStorage.setItem(EXPIRY_KEY, res.expiresAt);
        this.recomputeSecondsLeft();
      })
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

  changePassword(payload: ChangePasswordRequest): Observable<{ token: string; expiresAt: string }> {
    return this.http.post<{ token: string; expiresAt: string }>(`${this.base}/change-password`, payload).pipe(
      // الخادم يبطل الجلسات القديمة ويصدر رمزاً جديداً — نحفظه فوراً حتى لا يُطرد المستخدم
      tap(res => {
        localStorage.setItem(TOKEN_KEY, res.token);
        localStorage.setItem(EXPIRY_KEY, res.expiresAt);
        this.recomputeSecondsLeft();
      })
    );
  }

  clear(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    localStorage.removeItem(EXPIRY_KEY);
    this._user.set(null);
    this._secondsLeft.set(0);
  }

  private persist(token: string, expiresAt: string | Date, user: User): void {
    localStorage.setItem(TOKEN_KEY, token);
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
