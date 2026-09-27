import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { environment } from '../../../environments/environment';
import { RequestList } from './request-list';

describe('RequestList', () => {
  let fixture: ComponentFixture<RequestList>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RequestList],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    fixture = TestBed.createComponent(RequestList);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shows a loading state before the response arrives', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Loading requests');
    http
      .expectOne(`${environment.apiBaseUrl}/maintenance-requests`)
      .flush([]);
  });

  it('renders the returned requests in the table', () => {
    fixture.detectChanges();
    http
      .expectOne(`${environment.apiBaseUrl}/maintenance-requests`)
      .flush([
        {
          id: 1,
          siteId: 1,
          siteName: 'HQ Tower',
          title: 'HVAC compressor service',
          description: 'Noisy compressor',
          estimatedCost: 1500,
          actualCost: null,
          status: 'PendingApproval',
          raisedByUserId: 1,
          raisedByEmail: 'alice.requester@northgate.example',
          createdAtUtc: '2026-09-27T10:00:00Z',
          approvedAtUtc: null,
          approvedByUserId: null,
          completedAtUtc: null
        },
        {
          id: 2,
          siteId: 2,
          siteName: 'Riverside Depot',
          title: 'Hallway light replacement',
          description: 'Two fittings out',
          estimatedCost: 250,
          actualCost: 240.5,
          status: 'Completed',
          raisedByUserId: 1,
          raisedByEmail: 'alice.requester@northgate.example',
          createdAtUtc: '2026-09-26T09:00:00Z',
          approvedAtUtc: '2026-09-26T10:00:00Z',
          approvedByUserId: 2,
          completedAtUtc: '2026-09-27T08:00:00Z'
        }
      ]);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelectorAll('tbody tr').length).toBe(2);
    expect(element.textContent).toContain('HVAC compressor service');
    expect(element.textContent).toContain('HQ Tower');
    expect(element.textContent).toContain('PendingApproval');
    expect(element.textContent).toContain('240.50');
  });

  it('shows the empty state when the organization has no requests', () => {
    fixture.detectChanges();
    http
      .expectOne(`${environment.apiBaseUrl}/maintenance-requests`)
      .flush([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'No maintenance requests yet.'
    );
  });

  it('shows an error state with retry when the API call fails', () => {
    fixture.detectChanges();
    http
      .expectOne(`${environment.apiBaseUrl}/maintenance-requests`)
      .flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Could not load maintenance requests');
    expect(element.textContent).toContain('Retry');
  });
});
