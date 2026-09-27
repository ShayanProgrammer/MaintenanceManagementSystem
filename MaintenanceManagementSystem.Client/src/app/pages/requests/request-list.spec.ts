import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { environment } from '../../../environments/environment';
import { AuthService } from '../../auth/auth.service';
import { MaintenanceRequest } from '../../requests/requests.models';
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

describe('RequestList approval actions', () => {
  let fixture: ComponentFixture<RequestList>;
  let http: HttpTestingController;
  let component: {
    mayDecide(r: MaintenanceRequest): boolean;
    approve(r: MaintenanceRequest): void;
    startReject(r: MaintenanceRequest): void;
    cancelReject(): void;
    submitReject(r: MaintenanceRequest): void;
    rejectForm: {
      controls: {
        reason: { setValue(v: string): void; value: string };
      };
    };
  };

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
    component = fixture.componentInstance as unknown as typeof component;
  });

  afterEach(() => http.verify());

  const LIST_URL = `${environment.apiBaseUrl}/maintenance-requests`;

  function pendingRow(
    overrides: Partial<MaintenanceRequest> = {}
  ): MaintenanceRequest {
    return {
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
      completedAtUtc: null,
      ...overrides
    };
  }

  function flushList(rows: MaintenanceRequest[]): void {
    http.expectOne(LIST_URL).flush(rows);
  }

  /// Sets the signed-in user through the real AuthService (public API:
  /// /auth/me served by the testing controller).
  function loginAs(
    id: number,
    role: 'Requester' | 'Approver',
    email: string
  ): void {
    TestBed.inject(AuthService).loadCurrentUser().subscribe();
    http.expectOne(`${environment.apiBaseUrl}/auth/me`).flush({
      userId: id,
      organizationId: 1,
      email,
      role
    });
  }

  it('shows approval controls for an approver on someone else’s pending request', () => {
    fixture.detectChanges();
    flushList([pendingRow()]);
    loginAs(2, 'Approver', 'bob.approver@northgate.example');
    fixture.detectChanges();

    expect(component.mayDecide(pendingRow())).toBe(true);
    const buttons = (fixture.nativeElement as HTMLElement)
      .querySelectorAll('tbody button');
    expect(buttons.length).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('Approve');
    expect(fixture.nativeElement.textContent).toContain('Reject');
  });

  it('hides approval controls for a requester', () => {
    fixture.detectChanges();
    flushList([pendingRow()]);
    loginAs(1, 'Requester', 'alice.requester@northgate.example');
    fixture.detectChanges();

    expect(component.mayDecide(pendingRow())).toBe(false);
    expect(
      (fixture.nativeElement as HTMLElement).querySelectorAll('tbody button')
        .length
    ).toBe(0);
  });

  it('hides approval controls for an approver on their own pending request', () => {
    fixture.detectChanges();
    flushList([
      pendingRow({
        raisedByUserId: 2,
        raisedByEmail: 'bob.approver@northgate.example'
      })
    ]);
    loginAs(2, 'Approver', 'bob.approver@northgate.example');
    fixture.detectChanges();

    expect(
      (fixture.nativeElement as HTMLElement).querySelectorAll('tbody button')
        .length
    ).toBe(0);
  });

  it('approval posts the exact payload and refreshes the list', () => {
    fixture.detectChanges();
    flushList([pendingRow()]);
    loginAs(2, 'Approver', 'bob.approver@northgate.example');
    fixture.detectChanges();

    component.approve(pendingRow());

    const decision = http.expectOne(`${LIST_URL}/1/approvals`);
    expect(decision.request.method).toBe('POST');
    expect(decision.request.body).toEqual({ decision: 'approve' });
    decision.flush(pendingRow({ status: 'Approved' }));

    // The list is reloaded after the decision.
    flushList([pendingRow({ status: 'Approved' })]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Approved');
  });

  it('rejection requires a non-whitespace reason before posting', () => {
    fixture.detectChanges();
    flushList([pendingRow()]);
    loginAs(2, 'Approver', 'bob.approver@northgate.example');
    fixture.detectChanges();

    component.startReject(pendingRow());
    component.rejectForm.controls.reason.setValue('   ');
    component.submitReject(pendingRow());
    fixture.detectChanges();

    http.expectNone(`${LIST_URL}/1/approvals`);
    expect(fixture.nativeElement.textContent).toContain(
      'Reason is required.'
    );
  });

  it('rejection posts decision and reason, then refreshes the list', () => {
    fixture.detectChanges();
    flushList([pendingRow()]);
    loginAs(2, 'Approver', 'bob.approver@northgate.example');
    fixture.detectChanges();

    component.startReject(pendingRow());
    component.rejectForm.controls.reason.setValue('Budget exhausted');
    component.submitReject(pendingRow());

    const decision = http.expectOne(`${LIST_URL}/1/approvals`);
    expect(decision.request.method).toBe('POST');
    expect(decision.request.body).toEqual({
      decision: 'reject',
      reason: 'Budget exhausted'
    });
    decision.flush(pendingRow({ status: 'Rejected' }));

    flushList([pendingRow({ status: 'Rejected' })]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Rejected');
  });

  it('a 409 shows a stale-state message and refreshes the list', () => {
    fixture.detectChanges();
    flushList([pendingRow()]);
    loginAs(2, 'Approver', 'bob.approver@northgate.example');
    fixture.detectChanges();

    component.approve(pendingRow());
    http
      .expectOne(`${LIST_URL}/1/approvals`)
      .flush(null, { status: 409, statusText: 'Conflict' });

    // The refresh triggered by the 409 handler.
    flushList([pendingRow({ status: 'Rejected' })]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(
      'already been decided'
    );
  });

  it('a 403 shows a forbidden message', () => {
    fixture.detectChanges();
    flushList([pendingRow()]);
    loginAs(2, 'Approver', 'bob.approver@northgate.example');
    fixture.detectChanges();

    component.approve(pendingRow());
    http
      .expectOne(`${LIST_URL}/1/approvals`)
      .flush(null, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'not allowed to decide'
    );
  });
});
