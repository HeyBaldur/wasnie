export interface TokenPair {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
}

export interface AuthResult {
  userId: string;
  email: string;
  tenantId: string;
  tenantSlug: string;
  roles: string[];
  tokens: TokenPair | null;
  requiresTwoFactor?: boolean;
  twoFactorChallengeToken?: string;
}

export interface LoginRequest {
  email: string;
  password: string;

  /**
   * KAN-91. The workspace's identifier — its slug, e.g. `wasnie-ldta-polska`.
   *
   * Optional: an address that belongs to exactly one workspace never needs it, which is every
   * account today. It is only required once the same address is in two.
   */
  organizationId?: string;
}

export interface RegisterTenantRequest {
  tenantName: string;
  tenantSlug: string;
  adminEmail: string;
  adminPassword: string;
  adminFirstName: string;
  adminLastName: string;
}
