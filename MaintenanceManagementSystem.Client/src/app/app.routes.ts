import { Routes } from '@angular/router';

import { authGuard } from './auth/auth.guard';
import { Home } from './pages/home/home';
import { Login } from './pages/login/login';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'home' },
  { path: 'login', component: Login, title: 'Sign in' },
  { path: 'home', component: Home, canActivate: [authGuard], title: 'Home' },
  { path: '**', redirectTo: 'home' }
];
