import { Routes, type UrlMatchResult, type UrlSegment } from '@angular/router';
import { inject } from '@angular/core';
import { authGuard, crisisAccessGuard, signedOutGuard, therapistGuard } from './core/auth/auth.guard';
import { AuthService } from './core/auth/auth.service';
import { SessionService } from './core/session/session.service';
import { ShellComponent } from './layout/shell.component';

/**
 * /crisis and /crisis/:eventId (the link in every page Crisis.Worker sends, PageDispatcher) are one route, so
 * picking another event only changes the parameter and the console, its timers and its live connection stay.
 */
export function crisisMatcher(segments: UrlSegment[]): UrlMatchResult | null {
  if (segments[0]?.path !== 'crisis' || segments.length > 2) {
    return null;
  }
  return { consumed: segments, posParams: segments[1] ? { eventId: segments[1] } : {} };
}

export const routes: Routes = [
  { path: 'login', canActivate: [signedOutGuard], loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent), title: 'Tamaneen' },
  { path: 'recover', canActivate: [signedOutGuard], loadComponent: () => import('./features/auth/recover.component').then((m) => m.RecoverComponent), title: 'Tamaneen' },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    canActivateChild: [authGuard],
    children: [
      // Redirects run before the guards: signed out, the role (and so the home page) is not known yet, so go to
      // sign-in without a returnUrl and let sign-in pick the home of the role that signs in.
      { path: '', pathMatch: 'full', redirectTo: () => (inject(AuthService).signedIn() ? inject(SessionService).home() : '/login') },
      { path: 'dashboard', canMatch: [therapistGuard], loadComponent: () => import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent), title: 'Tamaneen' },
      { matcher: crisisMatcher, canMatch: [crisisAccessGuard], loadComponent: () => import('./features/crisis/crisis-console.component').then((m) => m.CrisisConsoleComponent), title: 'Tamaneen' },
      { path: 'forbidden', loadComponent: () => import('./features/forbidden/forbidden.component').then((m) => m.ForbiddenComponent), title: 'Tamaneen' },
      { path: 'home', loadComponent: () => import('./features/home/home.component').then((m) => m.HomeComponent), title: 'Tamaneen' },
      { path: '**', redirectTo: '' },
    ],
  },
];
