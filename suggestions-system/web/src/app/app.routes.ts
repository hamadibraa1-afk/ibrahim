import { Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard } from './core/guards/auth.guard';
import { UserRole } from './core/models/user.model';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/login/login.component').then(m => m.LoginComponent),
    title: 'تسجيل الدخول',
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./core/layout/shell.component').then(m => m.ShellComponent),
    children: [
      { path: '', redirectTo: 'my-proposals', pathMatch: 'full' },
      {
        path: 'my-proposals',
        loadComponent: () => import('./features/my-proposals/my-proposals.component').then(m => m.MyProposalsComponent),
        title: 'مقترحاتي',
      },
      {
        path: 'submit',
        loadComponent: () => import('./features/submission-form/submission-form.component').then(m => m.SubmissionFormComponent),
        title: 'تقديم مقترح جديد',
      },
      {
        path: 'proposals/:id',
        loadComponent: () => import('./features/proposal-detail/proposal-detail.component').then(m => m.ProposalDetailComponent),
        title: 'تفاصيل المقترح',
      },
      {
        path: 'proposals/:id/edit',
        loadComponent: () => import('./features/proposal-edit/proposal-edit.component').then(m => m.ProposalEditComponent),
        title: 'تعديل المقترح',
      },
      {
        path: 'screening',
        canActivate: [roleGuard],
        data: { roles: [UserRole.Screener, UserRole.Admin] },
        loadComponent: () => import('./features/screening-queue/screening-queue.component').then(m => m.ScreeningQueueComponent),
        title: 'قائمة الفرز',
      },
      {
        path: 'committee',
        canActivate: [roleGuard],
        data: { roles: [UserRole.CommitteeMember, UserRole.Admin] },
        loadComponent: () => import('./features/committee-list/committee-list.component').then(m => m.CommitteeListComponent),
        title: 'لجنة دراسة المقترحات',
      },
      {
        path: 'executive',
        canActivate: [roleGuard],
        data: { roles: [UserRole.Admin] },
        loadComponent: () => import('./features/executive-list/executive-list.component').then(m => m.ExecutiveListComponent),
        title: 'القرار التنفيذي',
      },
      {
        path: 'audit',
        canActivate: [roleGuard],
        data: { roles: [UserRole.Admin] },
        loadComponent: () => import('./features/audit-log/audit-log.component').then(m => m.AuditLogComponent),
        title: 'سجل الإجراءات',
      },
      {
        path: 'form-settings',
        canActivate: [roleGuard],
        data: { roles: [UserRole.Admin] },
        loadComponent: () => import('./features/form-settings/form-settings.component').then(m => m.FormSettingsComponent),
        title: 'إعدادات النموذج',
      },
      {
        path: 'dashboard',
        canActivate: [roleGuard],
        data: { roles: [UserRole.Admin] },
        loadComponent: () => import('./features/admin-dashboard/admin-dashboard.component').then(m => m.AdminDashboardComponent),
        title: 'لوحة التحكم',
      },
      {
        path: 'users',
        canActivate: [roleGuard],
        data: { roles: [UserRole.Admin] },
        loadComponent: () => import('./features/user-management/user-management.component').then(m => m.UserManagementComponent),
        title: 'إدارة المستخدمين',
      },
      {
        // متاح للجميع: الخادم يعيد لمدير النظام كل المقترحات، ولغيره ما أُسند إليه قياس أثره
        path: 'impact',
        loadComponent: () => import('./features/impact-tracking/impact-tracking.component').then(m => m.ImpactTrackingComponent),
        title: 'قياس الأثر والعائد',
      },
      {
        path: 'notifications',
        loadComponent: () => import('./features/notifications/notifications.component').then(m => m.NotificationsComponent),
        title: 'الإشعارات',
      },
      {
        path: 'profile',
        loadComponent: () => import('./features/profile/profile.component').then(m => m.ProfileComponent),
        title: 'الملف الشخصي',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
