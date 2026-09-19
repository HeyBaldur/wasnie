import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AssignmentsListComponent } from './assignments-list.component';
import { AssignmentsApiService } from '../services/assignments.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';

/**
 * The selection column exists for the bulk bar, so it goes when the bulk bar cannot act.
 *
 * ★★ THE DEFECT. Every action in that bar is gated on Assignments.Update or Assignments.Delete. A
 * Sales Rep holds neither, so the checkboxes rendered, ticked, highlighted the row — and produced a
 * bar containing nothing but "Cancel". A control that does nothing is the disabled-action pattern
 * §5.8 forbids, wearing a checkbox instead of a greyed-out button.
 *
 * ★★ ASSERTED THROUGH THE DOM, because a computed returning false proves nothing about whether the
 * markup reading it is on a branch that renders (§A3).
 */

const ROW = {
  id: 'a-1',
  payeeId: 'p-1',
  payeeFullName: 'Ana Garcia',
  payeeEmployeeCode: 'EMP-1',
  planId: 'pl-1',
  planName: 'Core Commission',
  startDate: '2026-01-01',
  endDate: '2026-12-31',
  status: 'Active',
};

const ONE_ROW = {
  items: [ROW], totalCount: 1, page: 1, pageSize: 10, totalPages: 1,
  hasNextPage: false, hasPreviousPage: false,
};

describe('AssignmentsListComponent — the selection column', () => {
  let fixture: ComponentFixture<AssignmentsListComponent>;

  const el = () => fixture.nativeElement as HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 5; i++) await Promise.resolve();
    fixture.detectChanges();
  }

  async function mountWith(permissions: string[]): Promise<void> {
    const api = jasmine.createSpyObj<AssignmentsApiService>('AssignmentsApiService', [
      'getAssignments', 'deactivateAssignment', 'bulkDeactivate', 'deactivationImpact',
    ]);
    api.getAssignments.and.returnValue(of(ONE_ROW) as never);
    api.deactivationImpact.and.returnValue(of({ strandedByCurrency: [], items: [] }));

    await TestBed.configureTestingModule({
      imports: [AssignmentsListComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssignmentsApiService, useValue: api },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['show']) },
        {
          provide: CurrentUserService,
          useValue: { hasPermission: (p: string) => permissions.includes(p) },
        },
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
    fixture.detectChanges();
    await settle();
  }

  afterEach(() => TestBed.resetTestingModule());

  const checkboxes = () => el().querySelectorAll('td.col-check input[type="checkbox"], th.col-check input[type="checkbox"]');

  /**
   * ★★ THE REPORTED CASE. A reader who can only look gets no checkbox at all — not a disabled one,
   * which would still say "this is for you, just not now".
   */
  it('draws no checkbox for a reader who can only read', async () => {
    await mountWith(['Assignments.Read']);

    expect(checkboxes().length).toBe(0);
  });

  it('draws them for somebody who can act on a selection', async () => {
    await mountWith(['Assignments.Read', 'Assignments.Update']);

    expect(checkboxes().length).toBeGreaterThan(0);
  });

  /**
   * ★★ DELETE ALONE IS ENOUGH, and this is the direction that would break quietly. Gating the column
   * on Update only would take it away from a role that can still bulk-delete — the opposite mistake,
   * and one nobody reports because nothing appears on screen to report.
   */
  it('draws them for somebody who can only bulk-delete', async () => {
    await mountWith(['Assignments.Read', 'Assignments.Delete']);

    expect(checkboxes().length).toBeGreaterThan(0);
  });

  /**
   * ★★ THE COLUMN COUNT FOLLOWS THE COLUMN. A hardcoded colspan would overrun the table by one the
   * moment the checkbox goes, and the filtered-empty row is exactly the state nobody screenshots.
   */
  it('keeps the header and the body the same width', async () => {
    await mountWith(['Assignments.Read']);

    const headers = el().querySelectorAll('thead th').length;
    const cells = el().querySelectorAll('tbody tr:first-child td').length;

    expect(headers).toBe(5);
    expect(cells).toBe(headers);
  });
});
