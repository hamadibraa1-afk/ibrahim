import { Routes } from '@angular/router';
import { HomeComponent } from './features/home/home.component';
import { ShellComponent } from './layout/shell.component';

export const routes: Routes = [
  {
    path: '',
    component: ShellComponent,
    children: [
      { path: '', pathMatch: 'full', component: HomeComponent, title: 'Tamaneen' },
      { path: '**', redirectTo: '' },
    ],
  },
];
