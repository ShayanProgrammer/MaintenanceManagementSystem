import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

/// <summary>
/// Attaches "Authorization: Bearer <token>" to API requests when the client
/// holds a token, and reacts to 401 responses by signing out and sending the
/// user back to the login page (expired or revoked token). 401s from the
/// login request itself are ignored — a wrong password is not a session
/// expiry, and redirecting from the login page to the login page would just
/// clear the typed form.
/// </summary>
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.token;
  const authenticated =
    token !== null && request.url.startsWith(environment.apiBaseUrl);

  return next(
    authenticated
      ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : request
  ).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse &&
          error.status === 401 &&
          !request.url.includes('/auth/login')) {
        auth.logout();
        router.navigate(['/login'], {
          queryParams: { returnUrl: router.url }
        });
      }
      return throwError(() => error);
    })
  );
};
