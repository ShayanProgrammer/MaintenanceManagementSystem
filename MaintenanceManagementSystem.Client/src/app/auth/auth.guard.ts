import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from './auth.service';

/// <summary>
/// Client-side UX guard: keeps unauthenticated visitors out of protected
/// routes by redirecting to /login with the attempted URL as returnUrl so
/// login can bring them back. Pure UX — the backend enforces authorization
/// independently for every API call.
/// </summary>
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated) {
    return true;
  }

  return router.createUrlTree(['/login'], {
    queryParams: { returnUrl: state.url }
  });
};
