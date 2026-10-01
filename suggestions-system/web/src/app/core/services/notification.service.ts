import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AppNotification } from '../models/notification.model';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/notifications`;

  private _unread = signal(0);
  readonly unreadCount = this._unread.asReadonly();

  list(take = 30): Observable<AppNotification[]> {
    return this.http.get<AppNotification[]>(`${this.base}?take=${take}`);
  }

  refreshUnread(): void {
    this.http.get<number>(`${this.base}/unread-count`).subscribe({
      next: n => this._unread.set(n),
      error: () => { /* تجاهل — لا نريد إزعاج المستخدم بفشل عدّاد */ },
    });
  }

  markRead(id: number): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/read`, {}).pipe(tap(() => this.refreshUnread()));
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>(`${this.base}/read-all`, {}).pipe(tap(() => this._unread.set(0)));
  }
}
