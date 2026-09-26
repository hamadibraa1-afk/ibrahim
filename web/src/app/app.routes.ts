import { Routes } from '@angular/router';
import { collectorGuard, employeeGuard, hrGuard, officeGuard, signedInGuard } from './core/auth';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./pages/login').then(m => m.LoginPage) },
  { path: 'select', canActivate: [signedInGuard], loadComponent: () => import('./pages/select-module').then(m => m.SelectModulePage) },
  { path: 'r/:token', loadComponent: () => import('./pages/public/rate').then(m => m.RatePage) },
  { path: 'r/:token/feedback', loadComponent: () => import('./pages/public/feedback').then(m => m.FeedbackPage) },
  {
    path: 'admin', canActivate: [officeGuard],
    loadComponent: () => import('./layout/supervisor-shell').then(m => m.SupervisorShell),
    children: [
      { path: '', loadComponent: () => import('./pages/supervisor/dashboard').then(m => m.DashboardPage) },
      { path: 'employees', loadComponent: () => import('./pages/supervisor/employees').then(m => m.EmployeesPage) },
      { path: 'locations', loadComponent: () => import('./pages/supervisor/locations').then(m => m.LocationsPage) },
      { path: 'shifts', loadComponent: () => import('./pages/supervisor/shifts').then(m => m.ShiftsPage) },
      { path: 'schedule', loadComponent: () => import('./pages/supervisor/schedule').then(m => m.SchedulePage) },
      { path: 'attendance', loadComponent: () => import('./pages/supervisor/attendance').then(m => m.AttendancePage) },
      { path: 'requests', loadComponent: () => import('./pages/supervisor/requests').then(m => m.RequestsPage) },
      { path: 'feedback', loadComponent: () => import('./pages/supervisor/feedback').then(m => m.FeedbackAdminPage) },
      { path: 'allowances', loadComponent: () => import('./pages/supervisor/allowances').then(m => m.AllowancesPage) },
      { path: 'ratings', loadComponent: () => import('./pages/supervisor/ratings').then(m => m.RatingsPage) },
    ],
  },
  {
    path: 'hr', canActivate: [hrGuard],
    loadComponent: () => import('./layout/hr-shell').then(m => m.HrShell),
    children: [
      { path: '', loadComponent: () => import('./pages/hr/dashboard').then(m => m.HrDashboardPage) },
      { path: 'employees', loadComponent: () => import('./pages/hr/employees').then(m => m.HrEmployeesPage) },
      { path: 'attendance', loadComponent: () => import('./pages/supervisor/attendance').then(m => m.AttendancePage) },
      { path: 'discipline', loadComponent: () => import('./pages/hr/discipline').then(m => m.HrDisciplinePage) },
      { path: 'payroll', loadComponent: () => import('./pages/hr/payroll').then(m => m.HrPayrollPage) },
      { path: 'returns', loadComponent: () => import('./pages/hr/returns').then(m => m.HrReturnsPage) },
      { path: 'reports', loadComponent: () => import('./pages/hr/reports').then(m => m.HrReportsPage) },
      { path: 'org', loadComponent: () => import('./pages/hr/org').then(m => m.HrOrgPage) },
      { path: 'schedules', loadComponent: () => import('./pages/hr/schedules').then(m => m.HrSchedulesPage) },
      { path: 'settings', loadComponent: () => import('./pages/hr/settings').then(m => m.HrSettingsPage) },
    ],
  },
  {
    path: 'my', canActivate: [employeeGuard],
    loadComponent: () => import('./layout/my-shell').then(m => m.MyShell),
    children: [
      { path: '', loadComponent: () => import('./pages/my/overview').then(m => m.MyOverviewPage) },
      { path: 'attendance', loadComponent: () => import('./pages/my/attendance').then(m => m.MyAttendancePage) },
      { path: 'requests', loadComponent: () => import('./pages/collector/my-requests').then(m => m.MyRequestsPage) },
      { path: 'payslips', loadComponent: () => import('./pages/my/payslips').then(m => m.MyPayslipsPage) },
      { path: 'profile', loadComponent: () => import('./pages/my/profile').then(m => m.MyProfilePage) },
    ],
  },
  {
    path: 'me', canActivate: [collectorGuard],
    loadComponent: () => import('./layout/collector-shell').then(m => m.CollectorShell),
    children: [
      { path: '', loadComponent: () => import('./pages/collector/today').then(m => m.TodayPage) },
      { path: 'schedule', loadComponent: () => import('./pages/collector/my-schedule').then(m => m.MySchedulePage) },
      { path: 'requests', loadComponent: () => import('./pages/collector/my-requests').then(m => m.MyRequestsPage) },
    ],
  },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'login' },
];
