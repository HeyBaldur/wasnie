import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import {
  WsPageHeaderComponent,
  WsCardComponent,
  WsButtonComponent,
  WsInputComponent,
} from '../../../shared/ui';
import { WsToastService } from '../../../shared/ui/ws-toast/ws-toast.service';
import { ProfileService, ProfileDto, TwoFactorStatusDto } from '../services/profile.service';

type TwoFactorStep = 'status' | 'setup' | 'confirm' | 'recoveryCodes' | 'disable' | 'regenCodes';

@Component({
  selector: 'app-manage-profile',
  standalone: true,
  imports: [
    FormsModule,
    TranslatePipe,
    AppShellComponent,
    WsPageHeaderComponent,
    WsCardComponent,
    WsButtonComponent,
    WsInputComponent,
  ],
  templateUrl: './manage-profile.component.html',
  styleUrl: './manage-profile.component.scss',
})
export class ManageProfileComponent implements OnInit {
  private readonly profileService = inject(ProfileService);
  private readonly toast = inject(WsToastService);

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly profile = signal<ProfileDto | null>(null);

  // Name section
  readonly firstName = signal('');
  readonly lastName = signal('');
  readonly savingName = signal(false);
  readonly nameIsMissing = computed(() => {
    const p = this.profile();
    return p !== null && !p.firstName && !p.lastName;
  });

  // Password section
  readonly currentPassword = signal('');
  readonly newPassword = signal('');
  readonly confirmNewPassword = signal('');
  readonly savingPassword = signal(false);
  readonly currentPasswordError = signal('');
  readonly passwordError = signal('');

  // Email change section
  readonly newEmail = signal('');
  readonly requestingEmailChange = signal(false);
  readonly emailChangeError = signal('');

  // 2FA section
  readonly twoFactorStatus = signal<TwoFactorStatusDto | null>(null);
  readonly twoFactorStep = signal<TwoFactorStep>('status');
  readonly twoFactorSecret = signal('');
  readonly twoFactorQrDataUrl = signal('');
  readonly twoFactorCode = signal('');
  readonly twoFactorError = signal('');
  readonly twoFactorLoading = signal(false);
  readonly recoveryCodesToShow = signal<string[]>([]);
  // Disable / regen: requires password + code
  readonly tfaPassword = signal('');
  readonly tfaCode = signal('');
  readonly tfaActionError = signal('');

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.profileService.getProfile().subscribe({
      next: profile => {
        this.profile.set(profile);
        this.firstName.set(profile.firstName);
        this.lastName.set(profile.lastName);
        this.newEmail.set(profile.email);
        this.loading.set(false);
        this.loadTwoFactorStatus();
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }

  loadTwoFactorStatus(): void {
    this.profileService.getTwoFactorStatus().subscribe({
      next: status => this.twoFactorStatus.set(status),
    });
  }

  saveName(): void {
    const first = this.firstName().trim();
    const last = this.lastName().trim();
    if (!first || !last) return;

    this.savingName.set(true);
    this.profileService.updateName(first, last).subscribe({
      next: () => {
        this.savingName.set(false);
        this.toast.show('PROFILE.NAME_SAVED', 'success');
      },
      error: () => {
        this.savingName.set(false);
        this.toast.show('PROFILE.NAME_ERROR', 'error');
      },
    });
  }

  changePassword(): void {
    this.passwordError.set('');
    this.currentPasswordError.set('');

    const newPw = this.newPassword();
    const confirmPw = this.confirmNewPassword();

    if (!newPw || !confirmPw || !this.currentPassword()) return;

    if (newPw !== confirmPw) {
      this.passwordError.set('PROFILE.PASSWORDS_MISMATCH');
      return;
    }

    this.savingPassword.set(true);
    this.profileService.changePassword(this.currentPassword(), newPw, confirmPw).subscribe({
      next: () => {
        this.savingPassword.set(false);
        this.currentPassword.set('');
        this.newPassword.set('');
        this.confirmNewPassword.set('');
        this.toast.show('PROFILE.PASSWORD_CHANGED', 'success');
      },
      error: (err) => {
        this.savingPassword.set(false);
        const msg = err?.error?.message ?? 'PROFILE.PASSWORD_ERROR';
        this.currentPasswordError.set(msg);
      },
    });
  }

  contactSupport(): void {
    window.location.href = 'mailto:support@wasnie.com';
  }

  contactPrivacy(): void {
    window.location.href = 'mailto:privacy@wasnie.io';
  }

  requestEmailChange(): void {
    this.emailChangeError.set('');
    const email = this.newEmail().trim();
    if (!email) return;

    this.requestingEmailChange.set(true);
    this.profileService.requestEmailChange(email).subscribe({
      next: () => {
        this.requestingEmailChange.set(false);
        this.toast.show('PROFILE.EMAIL_CHANGE_SENT', 'success');
        this.load();
      },
      error: (err) => {
        this.requestingEmailChange.set(false);
        const msg = err?.error?.message ?? 'PROFILE.EMAIL_CHANGE_ERROR';
        this.emailChangeError.set(msg);
      },
    });
  }

  // ── 2FA actions ────────────────────────────────────────────────────────────

  startSetup(): void {
    this.twoFactorError.set('');
    this.twoFactorLoading.set(true);

    this.profileService.getTwoFactorSetup().subscribe({
      next: async (setup) => {
        this.twoFactorSecret.set(setup.secret);
        try {
          const { default: QRCode } = await import('qrcode');
          const dataUrl = await QRCode.toDataURL(setup.otpauthUri, { width: 200, margin: 1 });
          this.twoFactorQrDataUrl.set(dataUrl);
        } catch {
          this.twoFactorQrDataUrl.set('');
        }
        this.twoFactorLoading.set(false);
        this.twoFactorStep.set('setup');
      },
      error: () => {
        this.twoFactorLoading.set(false);
        this.twoFactorError.set('TWO_FACTOR.SETUP_ERROR');
      },
    });
  }

  confirmEnable(): void {
    const code = this.twoFactorCode().trim();
    if (!code) return;

    this.twoFactorError.set('');
    this.twoFactorLoading.set(true);

    this.profileService.enableTwoFactor(code).subscribe({
      next: (result) => {
        this.twoFactorLoading.set(false);
        this.recoveryCodesToShow.set(result.recoveryCodes);
        this.twoFactorCode.set('');
        this.twoFactorStep.set('recoveryCodes');
        this.loadTwoFactorStatus();
      },
      error: (err) => {
        this.twoFactorLoading.set(false);
        const msg = err?.error?.message ?? 'TWO_FACTOR.ENABLE_ERROR';
        this.twoFactorError.set(msg);
      },
    });
  }

  finishSetup(): void {
    this.recoveryCodesToShow.set([]);
    this.twoFactorStep.set('status');
    this.toast.show('TWO_FACTOR.ENABLED_TOAST', 'success');
  }

  startDisable(): void {
    this.tfaPassword.set('');
    this.tfaCode.set('');
    this.tfaActionError.set('');
    this.twoFactorStep.set('disable');
  }

  confirmDisable(): void {
    const pw = this.tfaPassword().trim();
    const code = this.tfaCode().trim();
    if (!pw || !code) return;

    this.twoFactorLoading.set(true);
    this.tfaActionError.set('');

    this.profileService.disableTwoFactor(pw, code).subscribe({
      next: () => {
        this.twoFactorLoading.set(false);
        this.twoFactorStep.set('status');
        this.loadTwoFactorStatus();
        this.toast.show('TWO_FACTOR.DISABLED_TOAST', 'success');
      },
      error: (err) => {
        this.twoFactorLoading.set(false);
        const msg = err?.error?.message ?? 'TWO_FACTOR.DISABLE_ERROR';
        this.tfaActionError.set(msg);
      },
    });
  }

  startRegenCodes(): void {
    this.tfaPassword.set('');
    this.tfaCode.set('');
    this.tfaActionError.set('');
    this.twoFactorStep.set('regenCodes');
  }

  confirmRegenCodes(): void {
    const pw = this.tfaPassword().trim();
    const code = this.tfaCode().trim();
    if (!pw || !code) return;

    this.twoFactorLoading.set(true);
    this.tfaActionError.set('');

    this.profileService.regenerateRecoveryCodes(pw, code).subscribe({
      next: (result) => {
        this.twoFactorLoading.set(false);
        this.recoveryCodesToShow.set(result.recoveryCodes);
        this.twoFactorStep.set('recoveryCodes');
        this.loadTwoFactorStatus();
      },
      error: (err) => {
        this.twoFactorLoading.set(false);
        const msg = err?.error?.message ?? 'TWO_FACTOR.REGEN_ERROR';
        this.tfaActionError.set(msg);
      },
    });
  }

  cancelTwoFactorAction(): void {
    this.twoFactorStep.set('status');
    this.twoFactorCode.set('');
    this.twoFactorError.set('');
    this.tfaPassword.set('');
    this.tfaCode.set('');
    this.tfaActionError.set('');
  }
}
