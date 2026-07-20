import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { QuotasStore } from '../state/quotas.store';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { QuotaStatusVariantPipe, QuotaStatusLabelPipe } from '../../../shared/pipes/quota-status.pipe';
import {
  WsPageHeaderComponent,
  WsBadgeComponent,
  WsButtonComponent,
  WsConfirmationModalComponent,
} from '../../../shared/ui';

@Component({
  selector: 'app-quota-detail',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    RouterLink,
    TranslateModule,
    CurrencyFormatPipe,
    DateFormatPipe,
    WsPageHeaderComponent,
    WsBadgeComponent,
    WsButtonComponent,
    WsConfirmationModalComponent,
    QuotaStatusVariantPipe,
    QuotaStatusLabelPipe,
  ],
  templateUrl: './quota-detail.component.html',
  styleUrl: './quota-detail.component.scss',
})
export class QuotaDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  readonly store = inject(QuotasStore);
  private readonly toast = inject(ToastService);

  readonly quotaId = this.route.snapshot.paramMap.get('quotaId')!;

  readonly closeOpen = signal(false);
  readonly closeSaving = signal(false);

  ngOnInit(): void {
    this.store.loadQuota(this.quotaId);
  }

  async onActivate(): Promise<void> {
    try {
      await this.store.activateQuota(this.quotaId);
      this.toast.show('QUOTAS.TOAST_ACTIVATED', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  onClose(): void {
    this.closeOpen.set(true);
  }

  async onConfirmClose(): Promise<void> {
    this.closeSaving.set(true);
    try {
      await this.store.closeQuota(this.quotaId);
      this.toast.show('QUOTAS.TOAST_CLOSED', 'success');
      this.closeOpen.set(false);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.closeSaving.set(false);
    }
  }
}
