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
}
