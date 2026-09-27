import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AuthService } from '../../auth/auth.service';

/// <summary>
/// The authenticated home page: shows the signed-in user's information
/// (loaded from GET /api/auth/me by the shell) and links to the maintenance
/// request screens. Logout lives in the shell header.
/// </summary>
@Component({
  selector: 'app-home',
  imports: [RouterLink],
  templateUrl: './home.html',
  styleUrl: './home.css'
})
export class Home {
  private readonly auth = inject(AuthService);

  protected readonly user = this.auth.user;
}
