import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AcceptInvitationRequest,
  Invitation,
  InvitationPreview,
  InviteUserRequest,
  TenantRole,
  TenantUsersResponse,
} from '../models/user.model';

/**
 * KAN-32. Components never inject HttpClient — this service owns the HTTP for the users feature.
 */
@Injectable({ providedIn: 'root' })
export class UsersApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/users';

  /**
   * People, invitations and seats in ONE request.
   *
   * ★ Not three calls. Fetched separately they can disagree — a seat counted against an invitation
   * that was revoked between the two round trips — and the screen would show a contradiction it
   * invented itself.
   */
  getUsers(): Observable<TenantUsersResponse> {
    return this.http.get<TenantUsersResponse>(this.base);
  }

  invite(request: InviteUserRequest): Observable<Invitation> {
    return this.http.post<Invitation>(`${this.base}/invitations`, request);
  }

  resendInvitation(invitationId: string): Observable<Invitation> {
    return this.http.post<Invitation>(`${this.base}/invitations/${invitationId}/resend`, {});
  }

  revokeInvitation(invitationId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/invitations/${invitationId}`);
  }

  deactivate(userId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${encodeURIComponent(userId)}/deactivate`, {});
  }

  reactivate(userId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${encodeURIComponent(userId)}/reactivate`, {});
  }

  /** Ends the membership. Not the same as deactivate — see RemoveUserCommand. */
  remove(userId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${encodeURIComponent(userId)}`);
  }

  changeRole(userId: string, role: TenantRole): Observable<void> {
    return this.http.put<void>(`${this.base}/${encodeURIComponent(userId)}/role`, { role });
  }
}

/**
 * The two PUBLIC routes, kept in their own service.
 *
 * ★ Separate from UsersApiService on purpose: these are the only calls in the feature made by
 * somebody with no session, and mixing them in would invite a future method to be written against
 * `/api/users` and quietly fail for the one caller that matters here.
 */
@Injectable({ providedIn: 'root' })
export class InvitationsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/invitations';

  preview(token: string): Observable<InvitationPreview> {
    return this.http.get<InvitationPreview>(`${this.base}/${encodeURIComponent(token)}`);
  }

  accept(request: AcceptInvitationRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.base}/accept`, request);
  }
}
