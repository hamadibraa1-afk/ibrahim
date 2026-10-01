import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * يدير دورة حياة الجلسة:
 *  - عدّاد تنازلي دقيق للوقت المتبقي
 *  - تمديد تلقائي عند نشاط المستخدم (جلسة منزلقة)
 *  - تحذير قبل انتهاء الجلسة بدقيقتين مع خيار المتابعة
 *  - خروج تلقائي فور الانتهاء
 *  - مزامنة بين تبويبات المتصفح: الخروج من تبويب يُخرج البقية
 */
@Injectable({ providedIn: 'root' })
export class SessionManagerService {
  private auth = inject(AuthService);
  private router = inject(Router);

  /** يظهر التحذير عندما يتبقى أقل من هذه المدة. */
  private readonly warnAtSeconds = 120;
  /** لا نمدّد أكثر من مرة كل دقيقتين مهما كثر النشاط. */
  private readonly refreshThrottleMs = 120_000;

  readonly showWarning = signal(false);
  readonly secondsLeft = this.auth.secondsLeft;

  private ticker?: ReturnType<typeof setInterval>;
  private lastRefresh = 0;
  private started = false;

  start(): void {
    if (this.started) return;
    this.started = true;

    this.ticker = setInterval(() => this.tick(), 1000);

    // نشاط المستخدم يمدّد الجلسة
    ['click', 'keydown', 'scroll', 'mousemove'].forEach(evt =>
      window.addEventListener(evt, this.onActivity, { passive: true })
    );

    // مزامنة بين التبويبات
    window.addEventListener('storage', this.onStorage);
  }

  stop(): void {
    this.started = false;
    if (this.ticker) clearInterval(this.ticker);
    ['click', 'keydown', 'scroll', 'mousemove'].forEach(evt =>
      window.removeEventListener(evt, this.onActivity)
    );
    window.removeEventListener('storage', this.onStorage);
    this.showWarning.set(false);
  }

  /** يستدعيها زر "متابعة العمل" في نافذة التحذير. */
  extend(): void {
    this.auth.refreshSession().subscribe({
      next: () => { this.showWarning.set(false); this.lastRefresh = Date.now(); },
      error: () => this.forceLogout(),
    });
  }

  forceLogout(reason: 'expired' | 'manual' = 'expired'): void {
    const redirect = this.router.url.startsWith('/login') ? null : this.router.url;
    this.auth.logout();
    this.stop();
    this.router.navigate(['/login'], {
      queryParams: {
        ...(redirect ? { redirect } : {}),
        ...(reason === 'expired' ? { expired: '1' } : {}),
      },
    });
  }

  private tick(): void {
    if (!this.auth.isLoggedIn()) return;

    const left = this.auth.recomputeSecondsLeft();

    if (left <= 0) {
      this.forceLogout('expired');
      return;
    }
    this.showWarning.set(left <= this.warnAtSeconds);
  }

  private onActivity = (): void => {
    if (!this.auth.isLoggedIn()) return;
    // أثناء عرض التحذير ننتظر قرار المستخدم صراحة بدل التمديد الصامت
    if (this.showWarning()) return;

    const now = Date.now();
    if (now - this.lastRefresh < this.refreshThrottleMs) return;
    this.lastRefresh = now;

    this.auth.refreshSession().subscribe({ next: () => {}, error: () => {} });
  };

  private onStorage = (e: StorageEvent): void => {
    // خروج من تبويب آخر → اخرج هنا أيضاً
    if (e.key === 'sci.proposals.token' && e.newValue === null && this.auth.isLoggedIn()) {
      this.auth.clear();
      this.stop();
      this.router.navigate(['/login']);
    }
  };
}
