import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AcceptInvitationRequest,
  Invitation,
  InvitationPreview,
  InviteUserRequest,
  TenantRole,
  TenantUsersResponse,
  UnlinkedPayeesResponse,
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

  /**
   * The payees nobody owns yet — one page of them.
   *
   * ★ TWO INDEPENDENT INPUTS. `search` is what the admin is typing into the dropdown; `emailHint` is
   * the invitee's address, used only to ask the server to point at a likely match. They are sent
   * separately because the server answers them separately: folding them into one would make the
   * suggestion disappear the moment somebody starts typing a name.
   */
  unlinkedPayees(emailHint?: string, search?: string): Observable<UnlinkedPayeesResponse> {
    let params = new HttpParams();
    if (emailHint) params = params.set('email', emailHint);
    if (search) params = params.set('search', search);

    return this.http.get<UnlinkedPayeesResponse>(`${this.base}/unlinked-payees`, { params });
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

  /**
   * KAN-93. Attaches this login to a payee record, or detaches it when `payeeId` is null.
   *
   * ★ ONE METHOD FOR BOTH DIRECTIONS, matching the route. The link is single-valued — a person is one
   * payee or none — so "set it to nothing" is the honest spelling of unlinking, and a separate
   * `unlink()` would be a second caller that has to agree with this one about what none means.
   */
  linkPayee(userId: string, payeeId: string | null): Observable<void> {
    return this.http.put<void>(`${this.base}/${encodeURIComponent(userId)}/payee`, { payeeId });
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
