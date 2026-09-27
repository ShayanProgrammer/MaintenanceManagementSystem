import { Component, OnInit, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';

import { AuthService } from './auth/auth.service';

/// <summary>
/// Application shell: minimal header (identity + logout) around the routed
/// content. On startup it re-validates a persisted token by loading the
/// user's identity from /api/auth/me; an expired token is handled globally
/// by the interceptor.
/// </summary>
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly user = this.auth.user;

  ngOnInit(): void {
    this.auth.restoreSession();
  }

  logout(): void {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
