/**
 * KAN-32 — the users screen.
 *
 * The status of an invitation is computed on the SERVER and arrives already decided. The front end
 * must not recompute it from `expiresAt`: a browser clock that is minutes out would paint a row as
 * Pending that the API will refuse, and the user would be told two different things by one screen.
 */
export type InvitationStatus = 'Pending' | 'Accepted' | 'Expired' | 'Revoked';

/** The role names exactly as Identity stores them. */
export type TenantRole = 'TenantAdmin' | 'CompManager' | 'Manager' | 'Rep';

export const ASSIGNABLE_ROLES: readonly TenantRole[] = [
  'TenantAdmin',
  'CompManager',
  'Manager',
  'Rep',
];

export interface TenantUser {
  userId: string;
  email: string;
  firstName: string | null;
  lastName: string | null;
  role: TenantRole | null;
  isActive: boolean;
  emailConfirmed: boolean;
  createdAt: string;
  deactivatedAt: string | null;
  invitedByEmail: string | null;
}

export interface Invitation {
  id: string;
  email: string;
  role: TenantRole;
  status: InvitationStatus;
  createdAt: string;
  expiresAt: string;
  acceptedAt: string | null;
  resendCount: number;
  invitedByEmail: string | null;
}

/**
 * Seats used against seats allowed. `limit` null means unlimited, which is every plan today —
 * the screen shows no counter at all in that case rather than "3 of ∞".
 */
export interface SeatUsage {
  used: number;
  activeUsers: number;
  pendingInvitations: number;
  limit: number | null;
  hasRoom: boolean;
}

export interface TenantUsersResponse {
  users: TenantUser[];
  invitations: Invitation[];
  seats: SeatUsage;
}

export interface InviteUserRequest {
  email: string;
  role: TenantRole;

  /**
   * KAN-92. Which payee record this person IS. Null for somebody who does not get paid.
   *
   * Never derived from the email on the client either: the server may SUGGEST a match, and the
   * administrator is the one who picks.
   */
  payeeId?: string | null;
}

/** A payee no login owns yet — what the invite form can attach somebody to. */
export interface UnlinkedPayee {
  id: string;
  fullName: string;
  employeeCode: string | null;
  email: string | null;
}

/**
 * The pickable payees plus the one whose address matches.
 *
 * `suggested` is a hint and must never be pre-selected: a wrong match accepted by reflex shows one
 * person another person's pay.
 */
export interface UnlinkedPayeesResponse {
  payees: UnlinkedPayee[];
  suggested: UnlinkedPayee | null;
}

/** What the public accept page is told before anybody signs in. Carries no identifiers. */
export interface InvitationPreview {
  email: string;
  companyName: string;
  inviterName: string;
}

export interface AcceptInvitationRequest {
  token: string;
  firstName: string;
  lastName: string;
  password: string;
}
