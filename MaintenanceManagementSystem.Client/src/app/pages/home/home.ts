import { Component, inject } from '@angular/core';

import { AuthService } from '../../auth/auth.service';

/// <summary>
/// The authenticated home page: shows the signed-in user's information
/// (loaded from GET /api/auth/me by the shell). Maintenance request screens
/// arrive in later phases; logout lives in the shell header.
/// </summary>
@Component({
  selector: 'app-home',
  templateUrl: './home.html',
  styleUrl: './home.css'
})
export class Home {
  private readonly auth = inject(AuthService);

  protected readonly user = this.auth.user;
}
