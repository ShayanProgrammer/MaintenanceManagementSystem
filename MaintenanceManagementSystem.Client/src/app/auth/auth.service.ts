import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, map, tap } from 'rxjs';

import { environment } from '../../environments/environment';
import { CurrentUser, LoginResponse, MeResponse } from './auth.models';

const TOKEN_KEY = 'mms.auth.token';

/// <summary>
/// Simple client-side authentication state: holds the JWT and the current
/// user, persists the token in localStorage so a page refresh keeps the
/// session, and talks to /api/auth. This is UX only — the backend remains
/// the source of truth for authorization.
/// </summary>
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  private readonly currentUser = signal<CurrentUser | null>(null);

  /// <summary>Readonly view of the signed-in user (null when signed out).</summary>
  readonly user = this.currentUser.asReadonly();

  get token(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  get isAuthenticated(): boolean {
    return this.token !== null;
  }

  /// <summary>Exchanges credentials for a JWT and stores it. The 401 from a
  /// failed login propagates to the caller (the login page shows an error);
  /// the interceptor deliberately ignores 401s from this endpoint.</summary>
  login(email: string, password: string): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.apiBaseUrl}/auth/login`, { email, password })
      .pipe(
        tap(response => {
          localStorage.setItem(TOKEN_KEY, response.accessToken);
          this.currentUser.set(response.user);
        }));
  }

  /// <summary>Fetches the signed-in user's identity from /api/auth/me and
  /// publishes it. Used after a page refresh when only the token is known.</summary>
  loadCurrentUser(): Observable<CurrentUser> {
    return this.http.get<MeResponse>(`${this.apiBaseUrl}/auth/me`).pipe(
      map(me => ({
        id: me.userId,
        organizationId: me.organizationId,
        email: me.email,
        role: me.role
      })),
      tap(user => this.currentUser.set(user)));
  }

  /// <summary>If a token survives from an earlier page load but the user
  /// identity is not known yet, validate the token and fetch the identity.
  /// An expired token makes /me return 401, which the interceptor handles by
  /// signing out and returning to the login page.</summary>
  restoreSession(): void {
    if (this.token !== null && this.currentUser() === null) {
      this.loadCurrentUser().subscribe({ error: () => this.clearSession() });
    }
  }

  /// <summary>Removes the stored token and user identity.</summary>
  logout(): void {
    this.clearSession();
  }

  private clearSession(): void {
    localStorage.removeItem(TOKEN_KEY);
    this.currentUser.set(null);
  }
}
