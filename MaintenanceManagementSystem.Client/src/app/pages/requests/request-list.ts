import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import {
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { AuthService } from '../../auth/auth.service';
import {
  ApprovalDecision,
  MaintenanceRequest
} from '../../requests/requests.models';
import { RequestsService } from '../../requests/requests.service';
import { notWhitespaceValidator } from './request-create';

/// <summary>
/// Lists the authenticated user's organization's maintenance requests in a
/// plain table. Approvers get Approve/Reject actions on PendingApproval
/// requests raised by other users; requesters see status only. These checks
/// are presentation only — the backend enforces approver-only,
/// no-self-approval, lifecycle and tenancy. A 409 (someone else already
/// decided) shows a message and refreshes the list.
/// </summary>
@Component({
  selector: 'app-request-list',
  imports: [RouterLink, DatePipe, DecimalPipe, ReactiveFormsModule],
  templateUrl: './request-list.html',
  styleUrl: './request-list.css'
})
export class RequestList implements OnInit {
  private readonly requestsService = inject(RequestsService);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly fb = inject(NonNullableFormBuilder);

  /// <summary>null while loading, otherwise the returned rows.</summary>
  protected readonly requests = signal<MaintenanceRequest[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly createdId = signal<number | null>(null);

  protected readonly user = this.auth.user;

  /// <summary>Id of the row whose inline rejection form is open, if any.</summary>
  protected readonly rejectTargetId = signal<number | null>(null);
  protected readonly deciding = signal(false);
  protected readonly actionError = signal<string | null>(null);

  /// <summary>Rejection reason — required and whitespace-rejecting like the
  /// backend contract; the backend re-validates it authoritatively.</summary>
  protected readonly rejectForm = this.fb.group({
    reason: this.fb.control('', [
      Validators.required,
      notWhitespaceValidator
    ])
  });

  ngOnInit(): void {
    const created = this.route.snapshot.queryParamMap.get('created');
    if (created !== null && Number.isInteger(Number(created))) {
      this.createdId.set(Number(created));
    }
    this.load();
  }

  protected isApprover(): boolean {
    return this.user()?.role === 'Approver';
  }

  /// <summary>Presentation-only visibility rule: approver + still
  /// PendingApproval + not raised by the current user. The backend decides
  /// what is actually allowed.</summary>
  protected mayDecide(r: MaintenanceRequest): boolean {
    return (
      this.isApprover() &&
      r.status === 'PendingApproval' &&
      this.user()?.id !== r.raisedByUserId
    );
  }

  protected show(control: AbstractControlLike): boolean {
    return control.invalid && (control.touched || control.dirty);
  }

  protected approve(r: MaintenanceRequest): void {
    this.sendDecision(r.id, 'approve');
  }

  protected startReject(r: MaintenanceRequest): void {
    this.actionError.set(null);
    this.rejectForm.controls.reason.reset();
    this.rejectTargetId.set(r.id);
  }

  protected cancelReject(): void {
    this.rejectTargetId.set(null);
    this.rejectForm.controls.reason.reset();
  }

  protected submitReject(r: MaintenanceRequest): void {
    if (this.rejectForm.controls.reason.invalid) {
      this.rejectForm.controls.reason.markAsTouched();
      return;
    }
    this.sendDecision(r.id, 'reject', this.rejectForm.controls.reason.value);
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

  private sendDecision(
    id: number,
    decision: ApprovalDecision,
    reason?: string
  ): void {
    if (this.deciding()) {
      return;
    }
    this.deciding.set(true);
    this.actionError.set(null);
    this.requestsService.decide(id, decision, reason).subscribe({
      next: () => {
        this.deciding.set(false);
        this.rejectTargetId.set(null);
        this.rejectForm.controls.reason.reset();
        this.load();
      },
      error: (err: unknown) => {
        this.deciding.set(false);
        this.handleDecisionError(err, id);
      }
    });
  }

  private handleDecisionError(err: unknown, id: number): void {
    if (err instanceof HttpErrorResponse) {
      switch (err.status) {
        case 409:
          // Another user (or an earlier action) already decided it.
          this.actionError.set(
            'This request has already been decided. The list was refreshed.'
          );
          this.rejectTargetId.set(null);
          this.load();
          return;
        case 403:
          this.actionError.set(
            'You are not allowed to decide this request.'
          );
          return;
        case 404:
          this.actionError.set(
            'Request not found (it may belong to another organization).'
          );
          this.rejectTargetId.set(null);
          return;
        case 400: {
          const problem = err.error as
            | { title?: string; errors?: Record<string, string[]> }
            | null;
          const messages = problem?.errors
            ? Object.values(problem.errors).flat()
            : [];
          this.actionError.set(
            messages.length > 0
              ? `The decision was rejected: ${messages.join(' ')}`
              : 'The decision was rejected as invalid.'
          );
          return;
        }
      }
    }
    this.actionError.set('Could not record the decision. Please try again.');
  }
}

/// Minimal structural view of an AbstractControl for the template helper.
interface AbstractControlLike {
  invalid: boolean;
  touched: boolean;
  dirty: boolean;
}
