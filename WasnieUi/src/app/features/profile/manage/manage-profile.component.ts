import { Component, inject, signal, OnInit } from '@angular/core';
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
import { ProfileService, ProfileDto } from '../services/profile.service';

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
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
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
}
