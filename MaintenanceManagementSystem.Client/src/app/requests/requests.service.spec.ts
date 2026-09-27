import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { environment } from '../../environments/environment';
import { RequestsService } from './requests.service';

describe('RequestsService', () => {
  let service: RequestsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(RequestsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists maintenance requests from the organization-scoped endpoint', () => {
    service.list().subscribe();
    const request = http.expectOne(
      `${environment.apiBaseUrl}/maintenance-requests`
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.body).toBeNull();
    request.flush([]);
  });

  it('creates a request sending exactly the create-DTO fields', () => {
    let createdId: number | undefined;
    service
      .create({
        siteId: 1,
        title: 'Hallway light replacement',
        description: 'Two fittings out',
        estimatedCost: 250
      })
      .subscribe(created => (createdId = created.id));

    const request = http.expectOne(
      `${environment.apiBaseUrl}/maintenance-requests`
    );
    expect(request.request.method).toBe('POST');
    // The payload contains nothing the backend does not own: no
    // organizationId, userId, status or workflow/timestamp fields.
    expect(request.request.body).toEqual({
      siteId: 1,
      title: 'Hallway light replacement',
      description: 'Two fittings out',
      estimatedCost: 250
    });
    expect(Object.keys(request.request.body as object).sort()).toEqual([
      'description',
      'estimatedCost',
      'siteId',
      'title'
    ]);

    request.flush({ id: 42 });
    expect(createdId).toBe(42);
  });

  it('loads the caller’s organization sites for the dropdown', () => {
    service.listSites().subscribe();
    const request = http.expectOne(`${environment.apiBaseUrl}/sites`);
    expect(request.request.method).toBe('GET');
    request.flush([
      { id: 1, organizationId: 1, name: 'HQ Tower', description: null }
    ]);
  });
});
