import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { NotificationService } from '../../core/services/notification.service';
import { AppNotification, NotificationStyles } from '../../core/models/notification.model';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './notifications.component.html',
})
export class NotificationsComponent implements OnInit {
  private service = inject(NotificationService);
  private destroyRef = inject(DestroyRef);

  readonly NotificationStyles = NotificationStyles;
  items = signal<AppNotification[]>([]);
  loading = signal(true);

  ngOnInit() {
    this.load();
    // الإشعار الجديد يظهر في أعلى القائمة فور وصوله عبر SignalR دون إعادة تحميل
    this.service.received
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(n => this.items.update(list => [n, ...list.filter(x => x.id !== n.id)]));
  }

  load() {
    this.loading.set(true);
    this.service.list(50).subscribe({
      next: data => { this.items.set(data); this.loading.set(false); this.service.refreshUnread(); },
      error: () => this.loading.set(false),
    });
  }

  style(type: string) {
    return this.NotificationStyles[type] ?? { icon: '🔔', cls: 'bg-gray-50 text-gray-600' };
  }

  markRead(n: AppNotification) {
    if (n.isRead) return;
    this.service.markRead(n.id).subscribe(() => {
      this.items.update(list => list.map(x => x.id === n.id ? { ...x, isRead: true } : x));
    });
  }

  markAllRead() {
    this.service.markAllRead().subscribe(() => {
      this.items.update(list => list.map(x => ({ ...x, isRead: true })));
    });
  }

  get unreadTotal() { return this.items().filter(n => !n.isRead).length; }
}
