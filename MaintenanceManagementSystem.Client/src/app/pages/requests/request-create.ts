import { Component, OnInit, inject, signal } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators
} from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';

import { Site } from '../../requests/requests.models';
import { RequestsService } from '../../requests/requests.service';

/// <summary>Mirrors the backend's [NotWhitespace] rule: the services trim
/// title/description, so a whitespace-only value would be stored blank.</summary>
export function notWhitespaceValidator(
  control: AbstractControl
): ValidationErrors | null {
  return typeof control.value === 'string' && control.value.trim().length === 0
    ? { whitespace: true }
    : null;
}

/// <summary>
/// Creates a maintenance request with a reactive form. The payload contains
/// only the fields the backend create DTO accepts (siteId, title,
/// description, estimatedCost) — organization, raiser, status and timestamps
/// are server-assigned, and the approval threshold is evaluated only by the
/// backend; the status shown after creation is whatever the API returned.
/// </summary>
@Component({
  selector: 'app-request-create',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './request-create.html',
  styleUrl: './request-create.css'
})
export class RequestCreate implements OnInit {
  private readonly requestsService = inject(RequestsService);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly router = inject(Router);

  protected readonly sites = signal<Site[] | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /// <summary>Validation mirrors the backend create DTO: required site
  /// (>= 1), title 1..200, description 1..2000 (both rejecting
  /// whitespace-only), estimatedCost >= 0. No business rules beyond that —
  /// the threshold decision is never computed here.</summary>
  protected readonly form = this.fb.group({
    siteId: this.fb.control<number | null>(null, Validators.required),
    title: this.fb.control('', [
      Validators.required,
      Validators.maxLength(200),
      notWhitespaceValidator
    ]),
    description: this.fb.control('', [
      Validators.required,
      Validators.maxLength(2000),
      notWhitespaceValidator
    ]),
    estimatedCost: this.fb.control<number | null>(null, [
      Validators.required,
      Validators.min(0),
      Validators.max(999_999_999_999.99)
    ])
  });

  ngOnInit(): void {
    this.loadSites();
  }

  protected show(control: AbstractControl): boolean {
    return control.invalid && (control.touched || control.dirty);
  }

  protected retry(): void {
    this.loadSites();
  }

  protected submit(): void {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    this.submitting.set(true);
    this.errorMessage.set(null);
    this.requestsService
      .create({
        siteId: value.siteId as number,
        title: value.title,
        description: value.description,
        estimatedCost: value.estimatedCost as number
      })
      .subscribe({
        next: created =>
          this.router.navigate(['/requests'], {
            queryParams: { created: created.id }
          }),
        error: (error: unknown) => {
          this.submitting.set(false);
          this.errorMessage.set(describeCreateError(error));
        }
      });
  }

  private loadSites(): void {
    this.sites.set(null);
    this.loadError.set(null);
    this.requestsService.listSites().subscribe({
      next: sites => this.sites.set(sites),
      error: () => this.loadError.set('Could not load sites. Is the API running?')
    });
  }
}

/// <summary>Turns a failed create call into a readable message without
/// swallowing the backend's response (ProblemDetails validation errors are
/// surfaced verbatim).</summary>
function describeCreateError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 400) {
      const problem = error.error as
        | { title?: string; errors?: Record<string, string[]> }
        | null;
      const messages = problem?.errors
        ? Object.values(problem.errors).flat()
        : problem?.title
          ? [problem.title]
          : [];
      return messages.length > 0
        ? `The request was rejected: ${messages.join(' ')}`
        : 'The request was rejected as invalid.';
    }
    if (error.status === 404) {
      return 'The selected site was not found.';
    }
  }
  return 'Could not create the request. Please try again.';
}
