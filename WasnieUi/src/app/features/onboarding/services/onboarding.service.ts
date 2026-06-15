import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface QualificationPayload {
  country: string;
  phoneNumber: string;
  howHeardAboutUs: string;
  salesVolumeRange: string;
  currentSystem: string;
  legalAccepted: boolean;
}

@Injectable({ providedIn: 'root' })
export class OnboardingService {
  private readonly http = inject(HttpClient);

  qualify(payload: QualificationPayload): Observable<void> {
    return this.http.post<void>('/api/onboarding/qualify', payload);
  }
}
