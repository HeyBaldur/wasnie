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
}

export interface RegisterTenantRequest {
  tenantName: string;
  tenantSlug: string;
  adminEmail: string;
  adminPassword: string;
  adminFirstName: string;
  adminLastName: string;
}
