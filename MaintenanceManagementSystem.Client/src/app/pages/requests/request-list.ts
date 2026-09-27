import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { MaintenanceRequest } from '../../requests/requests.models';
import { RequestsService } from '../../requests/requests.service';

/// <summary>
/// Lists the authenticated user's organization's maintenance requests in a
/// plain table. Tenancy comes from the JWT server-side; a successful create
/// redirects here with ?created=<id> to show a small confirmation banner.
/// </summary>
@Component({
  selector: 'app-request-list',
  imports: [RouterLink, DatePipe, DecimalPipe],
  templateUrl: './request-list.html',
  styleUrl: './request-list.css'
})
export class RequestList implements OnInit {
  private readonly requestsService = inject(RequestsService);
  private readonly route = inject(ActivatedRoute);

  /// <summary>null while loading, otherwise the returned rows.</summary>
  protected readonly requests = signal<MaintenanceRequest[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly createdId = signal<number | null>(null);

  ngOnInit(): void {
    const created = this.route.snapshot.queryParamMap.get('created');
    if (created !== null && Number.isInteger(Number(created))) {
      this.createdId.set(Number(created));
    }
    this.load();
  }

  protected load(): void {
    this.requests.set(null);
    this.error.set(null);
    this.requestsService.list().subscribe({
      next: rows => this.requests.set(rows),
      error: () =>
        this.error.set('Could not load maintenance requests. Is the API running?')
    });
  }
}
