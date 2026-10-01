import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, Subject, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AppNotification, NotificationPush } from '../models/notification.model';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/notifications`;

  private _unread = signal(0);
  readonly unreadCount = this._unread.asReadonly();

  private readonly received$ = new Subject<AppNotification>();
  /** الإشعارات الواصلة لحظياً عبر SignalR — تعرضها صفحة الإشعارات فوراً. */
  readonly received = this.received$.asObservable();

  list(take = 30): Observable<AppNotification[]> {
    return this.http.get<AppNotification[]>(`${this.base}?take=${take}`);
  }

  /** مزامنة كاملة للعدّاد — عند بدء الاتصال وبعد إعادة الاتصال فقط، لا استطلاع دوري. */
  refreshUnread(): void {
    this.http.get<number>(`${this.base}/unread-count`).subscribe({
      next: n => this._unread.set(n),
      error: () => { /* تجاهل — لا نريد إزعاج المستخدم بفشل عدّاد */ },
    });
  }

  /** إشعار جديد دفعه الخادم مع العدد الصحيح غير المقروء. */
  applyPush(push: NotificationPush): void {
    this._unread.set(push.unreadCount);
    this.received$.next(push.notification);
  }

  setUnread(count: number): void {
    this._unread.set(count);
  }

  markRead(id: number): Observable<void> {
    // الخادم يدفع العدد الجديد لكل تبويبات المستخدم؛ نحدّث هنا تفاؤلياً أيضاً
    return this.http.post<void>(`${this.base}/${id}/read`, {}).pipe(
      tap(() => this._unread.update(n => Math.max(0, n - 1)))
    );
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>(`${this.base}/read-all`, {}).pipe(tap(() => this._unread.set(0)));
  }
}
