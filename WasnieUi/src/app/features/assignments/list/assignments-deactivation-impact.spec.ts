import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { AssignmentsListComponent } from './assignments-list.component';
import { AssignmentsApiService } from '../services/assignments.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { DeactivationImpact } from '../models/assignment.model';

/**
 * The deactivate dialog states the unpaid commission it would strand.
 *
 * ★ WHY. Deactivating is instant and silent; the consequence only shows up later as a pay run that
 *   produces nothing. €385,731.02 sat unreachable for weeks because six assignments had been switched
 *   off and no screen ever connected the two facts.
 *
 * These go through the DOM: a signal holding the right number proves nothing if the markup that would
 * print it is on a branch that never renders (§A3).
 */

const EMPTY_PAGE = {
  items: [], totalCount: 0, page: 1, pageSize: 10, totalPages: 0,
  hasNextPage: false, hasPreviousPage: false,
};

const NOTHING: DeactivationImpact = { strandedByCurrency: [], items: [] };

const STRANDED: DeactivationImpact = {
  strandedByCurrency: [{ amount: 385731.02, currency: 'EUR' }],
  items: [
    {
      assignmentId: 'a-1', payeeId: 'p-1', payeeName: 'Rudolph GeHard Chipellin',
      planId: 'pl-1', planName: 'Core Commission',
      creditCount: 41, amount: 366250, currency: 'EUR',
    },
    {
      assignmentId: 'a-2', payeeId: 'p-1', payeeName: 'Rudolph GeHard Chipellin',
      planId: 'pl-2', planName: 'EU Accelerator',
      creditCount: 12, amount: 19481.02, currency: 'EUR',
    },
  ],
};

describe('AssignmentsListComponent — what a deactivation would strand', () => {
  let fixture: ComponentFixture<AssignmentsListComponent>;
  let component: AssignmentsListComponent;
  let api: jasmine.SpyObj<AssignmentsApiService>;

  const el = () => fixture.nativeElement as HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 5; i++) await Promise.resolve();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    api = jasmine.createSpyObj<AssignmentsApiService>('AssignmentsApiService', [
      'getAssignments', 'deactivateAssignment', 'bulkDeactivate', 'deactivationImpact',
    ]);
    api.getAssignments.and.returnValue(of(EMPTY_PAGE) as never);
    api.deactivationImpact.and.returnValue(of(NOTHING));

    await TestBed.configureTestingModule({
      imports: [AssignmentsListComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssignmentsApiService, useValue: api },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['show']) },
        { provide: CurrentUserService, useValue: { hasPermission: () => true } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: new Map(), queryParamMap: new Map() },
            paramMap: of(new Map()),
            queryParams: of({}),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssignmentsListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await settle();
  });

  it('asks what would be stranded as the dialog opens, not after the click', async () => {
    component.onDeactivate('a-1');
    await settle();

    expect(api.deactivationImpact).toHaveBeenCalledWith({ assignmentIds: ['a-1'] });
    expect(api.deactivateAssignment).not.toHaveBeenCalled();
  });

  it('puts the amount and the plan in front of the reader', async () => {
    api.deactivationImpact.and.returnValue(of(STRANDED));

    component.onDeactivate('a-1');
    await settle();

    const text = el().textContent ?? '';
    expect(el().querySelector('.asg-strand')).not.toBeNull();
    expect(text).toContain('Core Commission');
    expect(text).toContain('EU Accelerator');
    expect(text).toContain('Rudolph GeHard Chipellin');
  });

  /**
   * ★ A DIALOG THAT WARNS EVERY TIME IS ONE NOBODY READS BY THE TIME IT MATTERS. Most deactivations
   *   strand nothing, and those must look exactly as they did before this existed.
   */
  it('stays silent when nothing would be stranded', async () => {
    component.onDeactivate('a-1');
    await settle();

    expect(el().querySelector('.asg-strand')).toBeNull();
    expect(component.hasImpact()).toBeFalse();
  });

  it('does not block the deactivation when the impact cannot be fetched', async () => {
    // The warning is an addition, never a gate: a failed lookup must not stop the administrator from
    // doing what they came to do.
    api.deactivationImpact.and.returnValue(throwError(() => new Error('boom')));

    component.onDeactivate('a-1');
    await settle();

    expect(component.deactivationImpact()).toBeNull();
    expect(el().querySelector('.asg-strand')).toBeNull();
    expect(component.deactivateOpen()).toBeTrue();
  });

  it('asks about every selected assignment on the bulk path', async () => {
    api.deactivationImpact.and.returnValue(of(STRANDED));
    component.store.selectedIds.set(new Set(['a-1', 'a-2']));

    component.onBulkDeactivate();
    await settle();

    const [body] = api.deactivationImpact.calls.mostRecent().args;
    expect(new Set(body.assignmentIds)).toEqual(new Set(['a-1', 'a-2']));
    expect(component.bulkDeactivateOpen()).toBeTrue();
  });

  it('caps the list and says how many are not shown', async () => {
    // A confirmation dialog is not a report; the rows exist to make the total believable.
    const many: DeactivationImpact = {
      strandedByCurrency: [{ amount: 700, currency: 'EUR' }],
      items: Array.from({ length: 8 }, (_, i) => ({
        assignmentId: `a-${i}`, payeeId: `p-${i}`, payeeName: `Payee ${i}`,
        planId: 'pl-1', planName: 'Core', creditCount: 1, amount: 100 - i, currency: 'EUR',
      })),
    };
    api.deactivationImpact.and.returnValue(of(many));

    component.onDeactivate('a-1');
    await settle();

    expect(component.impactShownItems().length).toBe(5);
    expect(component.impactHiddenCount()).toBe(3);
    expect(el().querySelectorAll('.asg-strand__list li').length).toBe(5);
  });

  it('reopening for another assignment does not show the previous warning', async () => {
    // The stale figure would be attributed to the wrong assignment, which is worse than none.
    api.deactivationImpact.and.returnValue(of(STRANDED));
    component.onDeactivate('a-1');
    await settle();
    expect(component.hasImpact()).toBeTrue();

    api.deactivationImpact.and.returnValue(of(NOTHING));
    component.onDeactivate('a-9');
    await settle();

    expect(component.hasImpact()).toBeFalse();
    expect(el().querySelector('.asg-strand')).toBeNull();
  });
});
