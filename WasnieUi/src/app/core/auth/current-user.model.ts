export type Tier = 'Free' | 'Starter' | 'Growth' | 'Scale' | 'Enterprise';

export interface CurrentUser {
  userId: string;
  email: string;
  role: string;
  tenantId: string;
  tenantSlug: string;
  tier: Tier;
  permissions: string[];
}
