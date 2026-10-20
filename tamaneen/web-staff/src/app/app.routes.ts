import { Routes } from '@angular/router';
import { inject } from '@angular/core';
import { authGuard, crisisAccessGuard, signedOutGuard, therapistGuard } from './core/auth/auth.guard';
import { SessionService } from './core/session/session.service';
import { LoginComponent } from './features/auth/login.component';
import { ShellComponent } from './layout/shell.component';

export const routes: Routes = [
  { path: 'login', component: LoginComponent, canActivate: [signedOutGuard], title: 'Tamaneen' },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    canActivateChild: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: () => inject(SessionService).home() },
      { path: 'dashboard', canMatch: [therapistGuard], loadComponent: () => import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent), title: 'Tamaneen' },
      { path: 'crisis', canMatch: [crisisAccessGuard], loadComponent: () => import('./features/crisis/crisis-console.component').then((m) => m.CrisisConsoleComponent), title: 'Tamaneen' },
      { path: 'home', loadComponent: () => import('./features/home/home.component').then((m) => m.HomeComponent), title: 'Tamaneen' },
      { path: '**', redirectTo: '' },
    ],
  },
];
