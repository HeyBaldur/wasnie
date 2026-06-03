import {
  Component,
  DestroyRef,
  inject,
  input,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule, DecimalPipe } from '@angular/common';
import { Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { timer, Subscription, of } from 'rxjs';
import { switchMap, catchError } from 'rxjs/operators';
import {
  TransactionsApiService,
  ProcessPendingScope,
  JobStatus,
  ProcessPendingResultSummary,
} from '../services/transactions.api.service';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsCardComponent,
} from '../../../shared/ui';
import { WsTooltipDirective } from '../../../shared/ui/ws-tooltip/ws-tooltip.directive';
import { IconComponent } from '../../../shared/components/icon/icon.component';

@Component({
  selector: 'app-process-pending',
  standalone: true,
  imports: [CommonModule, DecimalPipe, TranslateModule, WsButtonComponent, WsBadgeComponent, WsCardComponent, WsTooltipDirective, IconComponent],
  templateUrl: './process-pending.component.html',
  styleUrl: './process-pending.component.scss',
})
export class ProcessPendingComponent implements OnInit {
  private readonly txApi = inject(TransactionsApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);

  readonly scope = input.required<ProcessPendingScope>();
  readonly scopeId = input<string | null>(null);
  readonly periodStart = input<string | null>(null);
  readonly periodEnd = input<string | null>(null);

  readonly candidateCount = signal<number | null>(null);
  readonly countLoading = signal(false);

  readonly jobId = signal<string | null>(null);
  readonly jobStatus = signal<JobStatus | null>(null);
  readonly dispatching = signal(false);
  readonly cancelling = signal(false);
  readonly netError = signal(false);
  readonly skipLogOpen = signal(false);
  readonly copiedRef = signal<string | null>(null);

  get resultSummary(): ProcessPendingResultSummary | null {
    const raw = this.jobStatus()?.resultSummary;
    if (!raw) return null;
    try { return JSON.parse(raw) as ProcessPendingResultSummary; } catch { return null; }
  }

  private _polling: Subscription | null = null;

  readonly VOLUME_THRESHOLD = 5000;

  ngOnInit(): void {
    this._loadCount();
  }

  private _loadCount(): void {
    this.countLoading.set(true);
    this.txApi
      .getPendingCount(this.scope(), this.scopeId(), this.periodStart(), this.periodEnd())
      .subscribe({
        next: (res) => {
          this.candidateCount.set(res.count);
          this.countLoading.set(false);
        },
        error: () => this.countLoading.set(false),
      });
  }

  get isRunning(): boolean {
    const s = this.jobStatus()?.state;
    return s === 'Pending' || s === 'Running' || s === 'Cancelling';
  }

  get isDone(): boolean {
    const s = this.jobStatus()?.state;
    return s === 'Succeeded' || s === 'Cancelled' || s === 'Failed';
  }

  get progressPct(): number {
    const s = this.jobStatus();
    if (!s || s.progressTotal === 0) return 0;
    return Math.round((s.progressCurrent / s.progressTotal) * 100);
  }

  get showVolumeWarning(): boolean {
    const c = this.candidateCount();
    return c !== null && c > this.VOLUME_THRESHOLD && !this.jobId();
  }

  get estimatedMinutes(): number {
    const c = this.candidateCount() ?? 0;
    return Math.ceil((c / 100) * 3 / 60);
  }

  onProcessPending(): void {
    if (this.dispatching()) return;
    this.dispatching.set(true);

    this.txApi
      .processPending({
        scope: this.scope(),
        scopeId: this.scopeId(),
        periodStart: this.periodStart(),
        periodEnd: this.periodEnd(),
      })
      .subscribe({
        next: (res) => {
          this.jobId.set(res.jobId);
          this.dispatching.set(false);
          this._startPolling(res.jobId);
        },
        error: () => this.dispatching.set(false),
      });
  }

  private _startPolling(jobId: string): void {
    this._polling = timer(0, 1000)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        switchMap(() =>
          this.txApi.getJobStatus(jobId).pipe(
            catchError(() => {
              this.netError.set(true);
              return of(null as JobStatus | null);
            }),
          ),
        ),
      )
      .subscribe((s) => {
        if (!s) return;
        this.netError.set(false);
        this.jobStatus.set(s);
        if (s.state === 'Succeeded' || s.state === 'Cancelled' || s.state === 'Failed') {
          this._polling?.unsubscribe();
          // Refresh count after processing completes.
          if (s.state === 'Succeeded') this._loadCount();
        }
      });
  }

  onCancel(): void {
    const id = this.jobId();
    if (!id || this.cancelling()) return;
    this.cancelling.set(true);
    this.txApi.cancelJob(id).subscribe({
      next: () => this.cancelling.set(false),
      error: () => this.cancelling.set(false),
    });
  }

  copyRef(ref: string): void {
    navigator.clipboard.writeText(ref).then(() => {
      this.copiedRef.set(ref);
      setTimeout(() => this.copiedRef.set(null), 2000);
    });
  }

  onOpenInFilter(): void {
    const refs = this.resultSummary?.skipDetails.map(e => e.refNum).filter(Boolean).join(',');
    if (!refs) return;
    const url = this.router.serializeUrl(
      this.router.createUrlTree(['/transactions'], { queryParams: { refs } }),
    );
    window.open(url, '_blank', 'noopener');
  }
}
