import { TestBed, ComponentFixture } from '@angular/core/testing';
import { vi } from 'vitest';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { provideRouter } from '@angular/router';

import { environment } from '../../../environments/environment';
import { RequestsService } from '../../requests/requests.service';
import { RequestCreate } from './request-create';

/// The component's form and submit() are protected; the tests exercise them
/// through this structural view (cast via unknown, as TypeScript intends).
type Testable = {
  form: {
    invalid: boolean;
    controls: {
      siteId: { setValue(v: number | null): void };
      title: {
        setValue(v: string): void;
        errors: Record<string, unknown> | null;
      };
      description: { setValue(v: string): void };
      estimatedCost: {
        setValue(v: number | null): void;
        errors: Record<string, unknown> | null;
      };
    };
  };
  submit(): void;
};

describe('RequestCreate', () => {
  let fixture: ComponentFixture<RequestCreate>;
  let http: HttpTestingController;
  let component: Testable;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RequestCreate],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    fixture = TestBed.createComponent(RequestCreate);
    http = TestBed.inject(HttpTestingController);
    component = fixture.componentInstance as unknown as Testable;
  });

  afterEach(() => http.verify());

  /// Serves the site list the component loads on init.
  function flushSites(): void {
    http.expectOne(`${environment.apiBaseUrl}/sites`).flush([
      { id: 1, organizationId: 1, name: 'HQ Tower', description: null },
      { id: 2, organizationId: 1, name: 'Riverside Depot', description: null }
    ]);
    fixture.detectChanges();
  }

  function fillValidValues(): void {
    component.form.controls.siteId.setValue(1);
    component.form.controls.title.setValue('Hallway light replacement');
    component.form.controls.description.setValue('Two fittings out');
    component.form.controls.estimatedCost.setValue(250);
  }

  it('loads the organization sites for the dropdown', () => {
    fixture.detectChanges();
    flushSites();

    const options = (fixture.nativeElement as HTMLElement).querySelectorAll(
      'select option'
    );
    expect(options.length).toBe(3); // placeholder + 2 sites
    expect(options[1].textContent).toContain('HQ Tower');
    expect(options[2].textContent).toContain('Riverside Depot');
  });

  it('does not submit while the form is invalid', () => {
    fixture.detectChanges();
    flushSites();

    const createSpy = vi.spyOn(TestBed.inject(RequestsService), 'create');
    component.submit();
    fixture.detectChanges();

    expect(createSpy).not.toHaveBeenCalled();
    // All required messages become visible after the attempted submit.
    expect(fixture.nativeElement.textContent).toContain('Site is required.');
    expect(fixture.nativeElement.textContent).toContain('Title is required.');
    expect(fixture.nativeElement.textContent).toContain(
      'Estimated cost is required.'
    );
  });

  it('rejects a whitespace-only title like the backend does', () => {
    fixture.detectChanges();
    flushSites();

    component.form.controls.title.setValue('   ');

    expect(component.form.invalid).toBe(true);
    expect(component.form.controls.title.errors).toEqual({ whitespace: true });
  });

  it('rejects a negative estimated cost', () => {
    fixture.detectChanges();
    flushSites();

    component.form.controls.estimatedCost.setValue(-5);

    expect(component.form.controls.estimatedCost.errors?.['min']).toEqual({
      actual: -5,
      min: 0
    });
  });

  it('posts exactly the create-DTO fields and navigates to the list', () => {
    fixture.detectChanges();
    flushSites();
    fillValidValues();

    const navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate');
    component.submit();

    const request = http.expectOne(
      `${environment.apiBaseUrl}/maintenance-requests`
    );
    expect(request.request.body).toEqual({
      siteId: 1,
      title: 'Hallway light replacement',
      description: 'Two fittings out',
      estimatedCost: 250
    });
    request.flush({ id: 42 });

    expect(navigateSpy).toHaveBeenCalledWith(['/requests'], {
      queryParams: { created: 42 }
    });
  });

  it('surfaces backend validation errors instead of swallowing them', () => {
    fixture.detectChanges();
    flushSites();
    fillValidValues();

    component.submit();
    http
      .expectOne(`${environment.apiBaseUrl}/maintenance-requests`)
      .flush(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { EstimatedCost: ['The estimated cost is out of range.'] }
        },
        { status: 400, statusText: 'Bad Request' }
      );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'The estimated cost is out of range.'
    );
  });
});
