import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';
import { SessionManagerService } from '../services/session-manager.service';
import { UserRole, UserRoleLabels } from '../models/user.model';

interface NavItem { path: string; label: string; roles: UserRole[]; icon: string; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.component.html',
})
export class ShellComponent implements OnInit, OnDestroy {
  auth = inject(AuthService);
  private router = inject(Router);
  notifications = inject(NotificationService);
  session = inject(SessionManagerService);

  readonly UserRoleLabels = UserRoleLabels;
  readonly currentUser = this.auth.currentUser;
  readonly unreadCount = this.notifications.unreadCount;

  userMenuOpen = signal(false);
  private pollTimer?: ReturnType<typeof setInterval>;

  private readonly navItems: NavItem[] = [
    { path: '/my-proposals', label: 'مقترحاتي', roles: [UserRole.Employee, UserRole.Screener, UserRole.CommitteeMember, UserRole.Admin], icon: 'list' },
    { path: '/submit', label: 'تقديم مقترح', roles: [UserRole.Employee, UserRole.Screener, UserRole.CommitteeMember, UserRole.Admin], icon: 'plus' },
    { path: '/screening', label: 'قائمة الفرز', roles: [UserRole.Screener, UserRole.Admin], icon: 'check' },
    { path: '/committee', label: 'لجنة الدراسة', roles: [UserRole.CommitteeMember, UserRole.Admin], icon: 'users' },
    { path: '/executive', label: 'القرار التنفيذي', roles: [UserRole.Admin], icon: 'gavel' },
    { path: '/dashboard', label: 'لوحة التحكم', roles: [UserRole.Admin], icon: 'chart' },
    { path: '/audit', label: 'سجل الإجراءات', roles: [UserRole.Admin], icon: 'history' },
    { path: '/users', label: 'إدارة المستخدمين', roles: [UserRole.Admin], icon: 'settings' },
    { path: '/form-settings', label: 'إعدادات النموذج', roles: [UserRole.Admin], icon: 'sliders' },
  ];

  visibleNavItems = computed(() => {
    const role = this.currentUser()?.role;
    return role ? this.navItems.filter(i => i.roles.includes(role)) : [];
  });

  ngOnInit() {
    this.session.start();
    this.notifications.refreshUnread();
    // تحديث دوري لعدّاد الإشعارات كل 60 ثانية — يتوقف عند الخروج
    this.pollTimer = setInterval(() => {
      if (this.auth.isLoggedIn()) this.notifications.refreshUnread();
    }, 60_000);
  }

  /** الوقت المتبقي على الجلسة بصيغة د:ث لعرضه في نافذة التحذير. */
  get countdown(): string {
    const total = this.session.secondsLeft();
    const m = Math.floor(total / 60);
    const sec = total % 60;
    return `${m}:${sec.toString().padStart(2, '0')}`;
  }

  ngOnDestroy() {
    if (this.pollTimer) clearInterval(this.pollTimer);
  }

  initials(name: string): string {
    return name.trim().split(/\s+/).slice(0, 2).map(p => p.charAt(0)).join('');
  }

  logout() {
    this.userMenuOpen.set(false);
    // الحالة تُمسح بشكل متزامن داخل logout()، لذا التوجيه يعمل من أول محاولة
    this.auth.logout();
    this.session.stop();
    this.router.navigate(['/login']);
  }

  stayLoggedIn() { this.session.extend(); }
}
