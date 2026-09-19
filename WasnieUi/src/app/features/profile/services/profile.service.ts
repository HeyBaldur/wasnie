import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface ProfileDto {
  firstName: string;
  lastName: string;
  email: string;
  hasPendingEmailChange: boolean;
  companyName: string;
  organizationSlug: string;

  /**
   * Whether an administrator maintains this person's name and sign-in address rather than they
   * themselves - true when an administrator attached this login to a payee record.
   *
   * THE SCREEN HIDES THE TWO CARDS AND THE SERVER REFUSES ANYWAY. `PUT /api/profile/name` and the
   * email-change request carry no permission at all (self-service by design), so hiding alone would
   * be a screen saying no in front of an endpoint saying yes.
   */
  identityManagedByAdministrator: boolean;
}

export interface TwoFactorStatusDto {
  isEnabled: boolean;
  recoveryCodeCount: number;
}

export interface TwoFactorSetupDto {
  secret: string;
  otpauthUri: string;
}

export interface EnableTwoFactorResultDto {
  recoveryCodes: string[];
}

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/profile`;

  getProfile(): Observable<ProfileDto> {
    return this.http.get<ProfileDto>(this.base);
  }

  updateName(firstName: string, lastName: string): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.base}/name`, { firstName, lastName });
  }

  changePassword(currentPassword: string, newPassword: string, confirmNewPassword: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.base}/change-password`, {
      currentPassword,
      newPassword,
      confirmNewPassword,
    });
  }

  requestEmailChange(newEmail: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.base}/request-email-change`, { newEmail });
  }

  getTwoFactorStatus(): Observable<TwoFactorStatusDto> {
    return this.http.get<TwoFactorStatusDto>(`${this.base}/2fa/status`);
  }

  getTwoFactorSetup(): Observable<TwoFactorSetupDto> {
    return this.http.get<TwoFactorSetupDto>(`${this.base}/2fa/setup`);
  }

  enableTwoFactor(verificationCode: string): Observable<EnableTwoFactorResultDto> {
    return this.http.post<EnableTwoFactorResultDto>(`${this.base}/2fa/enable`, { verificationCode });
  }

  disableTwoFactor(password: string, code: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.base}/2fa/disable`, { password, code });
  }

  regenerateRecoveryCodes(password: string, code: string): Observable<{ recoveryCodes: string[] }> {
    return this.http.post<{ recoveryCodes: string[] }>(`${this.base}/2fa/recovery-codes`, { password, code });
  }
}
