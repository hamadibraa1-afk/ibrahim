import { Injectable, NgZone, inject, signal } from '@angular/core';
import { Observable, Subject, filter } from 'rxjs';
import {
  HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel,
} from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { NotificationPush, ProposalChangedEvent } from '../models/notification.model';
import { NotificationService } from './notification.service';

export type RealtimeState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/**
 * قناة الدفع اللحظي (SignalR) بدل الاستطلاع الدوري:
 *  - يصل الإشعار ويتحدّث عدّاد الجرس فور أي انتقال في سير العمل.
 *  - تُبلَّغ القوائم وصفحة التفاصيل بتغيّر المقترح لتحدّث نفسها.
 * المصادقة بكعكة الجلسة نفسها (HttpOnly) — لا يُرسَل أي رمز من السكربت.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private zone = inject(NgZone);
  private notifications = inject(NotificationService);

  private connection?: HubConnection;
  private retryTimer?: ReturnType<typeof setTimeout>;
  private retryAttempt = 0;
  private wanted = false;

  private readonly _state = signal<RealtimeState>('disconnected');
  readonly state = this._state.asReadonly();

  private readonly proposalChanged$ = new Subject<ProposalChangedEvent>();

  /** كل انتقال لأي مقترح يراه المستخدم. */
  readonly proposalChanges: Observable<ProposalChangedEvent> = this.proposalChanged$.asObservable();

  /** انتقالات مقترح بعينه — لصفحة التفاصيل. */
  changesFor(proposalId: number): Observable<ProposalChangedEvent> {
    return this.proposalChanges.pipe(filter(e => e.proposalId === proposalId));
  }

  start(): void {
    this.wanted = true;
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) return;

    this.connection ??= this.build();
    this.connect();
  }

  stop(): void {
    this.wanted = false;
    clearTimeout(this.retryTimer);
    this.retryAttempt = 0;
    const conn = this.connection;
    this.connection = undefined;
    this._state.set('disconnected');
    conn?.stop().catch(() => {});
  }

  private build(): HubConnection {
    const conn = new HubConnectionBuilder()
      .withUrl(environment.hubUrl, { withCredentials: true })
      // إعادة اتصال تلقائية سريعة عند الانقطاعات العابرة
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(environment.production ? LogLevel.None : LogLevel.Warning)
      .build();

    conn.on('notificationReceived', (push: NotificationPush) =>
      this.zone.run(() => this.notifications.applyPush(push)));
    conn.on('unreadCountChanged', (count: number) =>
      this.zone.run(() => this.notifications.setUnread(count)));
    conn.on('proposalChanged', (e: ProposalChangedEvent) =>
      this.zone.run(() => this.proposalChanged$.next(e)));

    conn.onreconnecting(() => this.zone.run(() => this._state.set('reconnecting')));
    conn.onreconnected(() => this.zone.run(() => {
      this._state.set('connected');
      // ما فات أثناء الانقطاع: نعيد مزامنة العدّاد مرة واحدة
      this.notifications.refreshUnread();
    }));
    // نفدت محاولات إعادة الاتصال التلقائية → نواصل المحاولة بتباعد متزايد
    conn.onclose(() => this.zone.run(() => {
      this._state.set('disconnected');
      if (this.wanted) this.scheduleRetry();
    }));
    return conn;
  }

  private connect(): void {
    const conn = this.connection;
    if (!conn) return;
    this._state.set('connecting');
    conn.start()
      .then(() => this.zone.run(() => {
        this.retryAttempt = 0;
        this._state.set('connected');
        this.notifications.refreshUnread();
      }))
      .catch(() => this.zone.run(() => {
        this._state.set('disconnected');
        if (this.wanted) this.scheduleRetry();
      }));
  }

  private scheduleRetry(): void {
    clearTimeout(this.retryTimer);
    const delay = Math.min(60_000, 2_000 * 2 ** this.retryAttempt++);
    this.retryTimer = setTimeout(() => {
      if (this.wanted && this.connection?.state === HubConnectionState.Disconnected) this.connect();
    }, delay);
  }
}
