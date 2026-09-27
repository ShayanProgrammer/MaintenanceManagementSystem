import { Routes } from '@angular/router';

import { authGuard } from './auth/auth.guard';
import { Home } from './pages/home/home';
import { Login } from './pages/login/login';
import { RequestCreate } from './pages/requests/request-create';
import { RequestList } from './pages/requests/request-list';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'home' },
  { path: 'login', component: Login, title: 'Sign in' },
  { path: 'home', component: Home, canActivate: [authGuard], title: 'Home' },
  {
    path: 'requests',
    component: RequestList,
    canActivate: [authGuard],
    title: 'Requests'
  },
  {
    path: 'requests/new',
    component: RequestCreate,
    canActivate: [authGuard],
    title: 'New request'
  },
  { path: '**', redirectTo: 'home' }
];
