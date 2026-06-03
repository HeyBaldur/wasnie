import { Component, inject, OnInit, signal, computed } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { AssignmentsApiService } from '../services/assignments.api.service';
import { Assignment } from '../models/assignment.model';
import { ProcessPendingComponent } from '../../transactions/process-pending/process-pending.component';
import {
  WsPageLayoutComponent,
  WsBadgeComponent,
  WsCardComponent,
  type BadgeVariant,
} from '../../../shared/ui';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';

@Component({
  selector: 'app-assignment-detail',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    TranslateModule,
    DateFormatPipe,
    WsPageLayoutComponent,
    WsBadgeComponent,
    WsCardComponent,
    ProcessPendingComponent,
    HasPermissionDirective,
  ],
  templateUrl: './assignment-detail.component.html',
  styleUrl: './assignment-detail.component.scss',
})
export class AssignmentDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly assignmentsApi = inject(AssignmentsApiService);

  readonly assignment = signal<Assignment | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly assignmentId = this.route.snapshot.paramMap.get('assignmentId')!;

  readonly periodStart = computed(() => this.assignment()?.effectiveStart ?? null);
  readonly periodEnd = computed(() => this.assignment()?.effectiveEnd ?? null);

  async ngOnInit(): Promise<void> {
    try {
      const a = await firstValueFrom(this.assignmentsApi.getAssignment(this.assignmentId));
      this.assignment.set(a);
    } catch {
      this.error.set('ASSIGNMENTS.ERROR_LOAD');
    } finally {
      this.loading.set(false);
    }
  }

  statusVariant(status: string): BadgeVariant {
    return status === 'Active' ? 'success' : 'neutral';
  }
}
