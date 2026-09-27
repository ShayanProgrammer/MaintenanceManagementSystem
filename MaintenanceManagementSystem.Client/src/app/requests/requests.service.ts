import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../environments/environment';
import {
  CreateMaintenanceRequest,
  MaintenanceRequest,
  Site
} from './requests.models';

/// <summary>
/// Thin API client for maintenance requests and sites. Every call relies on
/// the JWT (attached by the auth interceptor) for tenancy — no organization
/// or user identifiers are ever sent by the frontend.
/// </summary>
@Injectable({ providedIn: 'root' })
export class RequestsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  /// <summary>GET /api/maintenance-requests — the caller's organization's
  /// requests, scoped server-side from the token.</summary>
  list(): Observable<MaintenanceRequest[]> {
    return this.http.get<MaintenanceRequest[]>(
      `${this.apiBaseUrl}/maintenance-requests`);
  }

  /// <summary>POST /api/maintenance-requests — sends only the fields the
  /// backend create DTO accepts; 201 returns the created request with the
  /// server-assigned status.</summary>
  create(request: CreateMaintenanceRequest): Observable<MaintenanceRequest> {
    return this.http.post<MaintenanceRequest>(
      `${this.apiBaseUrl}/maintenance-requests`, request);
  }

  /// <summary>GET /api/sites — the caller's organization's sites for the
  /// create-form dropdown.</summary>
  listSites(): Observable<Site[]> {
    return this.http.get<Site[]>(`${this.apiBaseUrl}/sites`);
  }
}
