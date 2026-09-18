import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { QuotasStore } from '../state/quotas.store';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { QuotaStatusVariantPipe, QuotaStatusLabelPipe, QuotaPeriodExpiredPipe } from '../../../shared/pipes/quota-status.pipe';
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
    HasPermissionDirective,
    TranslateModule,
    CurrencyFormatPipe,
    DateFormatPipe,
    WsPageHeaderComponent,
    WsBadgeComponent,
    WsButtonComponent,
    WsConfirmationModalComponent,
    QuotaStatusVariantPipe,
    QuotaStatusLabelPipe,
    QuotaPeriodExpiredPipe,
  ],
  templateUrl: './quota-detail.component.html',
  styleUrl: './quota-detail.component.scss',
})
export class QuotaDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  readonly store = inject(QuotasStore);
  private readonly toast = inject(ToastService);
  private readonly currentUser = inject(CurrentUserService);

  readonly quotaId = this.route.snapshot.paramMap.get('quotaId')!;

  readonly closeOpen = signal(false);
  readonly closeSaving = signal(false);

  ngOnInit(): void {
    this.store.loadQuota(this.quotaId);
  }

  /**
   * Where the payee's name on this screen should lead.
   *
   * ★★ A REP HAS NO PAYEE PAGE, AND THE LINK MUST NOT PRETEND OTHERWISE. KAN-93 bug 1 hid the Payees
   * section from them completely — their own record is their dashboard, not a payee page — so the
   * unconditional link dropped them on Access Denied from a screen they were entitled to be on.
   *
   * ★★ IT IS SAFE TO SEND THEM TO THEIR OWN PROFILE because a rep can only ever open their own quota:
   * every quota handler applies PayeeAccessGuard, so the payee named here IS them. If that ever stops
   * being true this becomes wrong, which is why the reasoning is written down rather than assumed.
   *
   * ★ KEYED ON `Payees.Read`, THE SAME PERMISSION THE /payees ROUTE GUARD ASKS FOR. Anything else
   * would eventually disagree with it and offer a page that refuses the reader.
   */
  payeeLink(payeeId: string): unknown[] {
    return this.currentUser.hasPermission('Payees.Read')
      ? ['/payees', payeeId]
      : ['/profile'];
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
