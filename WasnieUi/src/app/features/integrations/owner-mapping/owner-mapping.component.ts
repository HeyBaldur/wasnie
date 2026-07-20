import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { map, Observable } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { HubSpotApiService } from '../services/hubspot.api.service';
import { UnresolvedCrmOwner } from '../models/crm-sync.model';
import {
  WsPageLayoutComponent,
  WsButtonComponent,
  WsTableComponent,
  WsEmptyStateComponent,
  WsBadgeComponent,
  WsModalComponent,
  WsSelectComponent,
  WsSegmentedControlComponent,
  type SelectOption,
  type SegOption,
} from '../../../shared/ui';

@Component({
  selector: 'app-crm-owner-mapping',
  standalone: true,
  imports: [
    FormsModule,
    RouterLink,
    TranslateModule,
    AppShellComponent,
    IconComponent,
    WsPageLayoutComponent,
    WsButtonComponent,
    WsTableComponent,
    WsEmptyStateComponent,
    WsBadgeComponent,
    WsModalComponent,
    WsSelectComponent,
    WsSegmentedControlComponent,
  ],
  templateUrl: './owner-mapping.component.html',
  styleUrl: './owner-mapping.component.scss',
})
export class CrmOwnerMappingComponent implements OnInit {
  private readonly api = inject(HubSpotApiService);
  private readonly payeesApi = inject(PayeesApiService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly owners = signal<UnresolvedCrmOwner[]>([]);

  // Link modal state
  readonly linkOpen = signal(false);
  readonly linking = signal(false);
  readonly activeOwner = signal<UnresolvedCrmOwner | null>(null);
  readonly selectedPayeeId = signal('');
  // 'reassign' = retroactively assign this owner's Unassigned (unpaid) transactions; 'future' = only new deals.
  readonly reassignChoice = signal<string>('reassign');

  readonly reassignOptions: SegOption[] = [
    { value: 'reassign', label: 'INTEGRATIONS.HUBSPOT.OWNERS.REASSIGN_EXISTING' },
    { value: 'future', label: 'INTEGRATIONS.HUBSPOT.OWNERS.FUTURE_ONLY' },
  ];

  /** Async payee search for the WsSelect (server-paged, debounced by the select itself). */
  readonly payeeSearch = (query: string): Observable<SelectOption[]> =>
    this.payeesApi
      .getPayees({ page: 1, pageSize: 20, search: query, sortBy: 'fullname', sortOrder: 'asc' })
      .pipe(
        map((result) =>
          result.items.map((p) => ({ value: p.id, label: `${p.fullName} (${p.employeeCode})` })),
        ),
      );

  get skeletonRows(): number[] {
    return Array.from({ length: 5 }, (_, i) => i);
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.getUnresolvedOwners().subscribe({
      next: (result) => {
        this.owners.set(result.owners);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.loadError.set(extractApiError(err));
      },
    });
  }

  openLink(owner: UnresolvedCrmOwner): void {
    this.activeOwner.set(owner);
    this.selectedPayeeId.set('');
    this.reassignChoice.set('reassign');
    this.linkOpen.set(true);
  }

  confirmLink(): void {
    const owner = this.activeOwner();
    const payeeId = this.selectedPayeeId();
    if (!owner || !payeeId) {
      return;
    }
    this.linking.set(true);
    this.api
      .linkOwner({
        ownerId: owner.ownerId,
        payeeId,
        reassignExistingUnassigned: this.reassignChoice() === 'reassign',
      })
      .subscribe({
        next: (result) => {
          this.linking.set(false);
          this.linkOpen.set(false);
          const reassigned = result.reassignedTransactions;
          this.toast.show(
            reassigned > 0
              ? 'INTEGRATIONS.HUBSPOT.OWNERS.TOAST_LINKED_REASSIGNED'
              : 'INTEGRATIONS.HUBSPOT.OWNERS.TOAST_LINKED',
            'success',
          );
          this.load();
        },
        error: (err) => {
          this.linking.set(false);
          this.toast.show(extractApiError(err), 'error');
        },
      });
  }
}
