export interface CurrentUser {
  userId: string;
  email: string;
  role: string;
  tenantId: string;
  tenantSlug: string;
  /** KAN-77: the subscribed plan code (e.g. "pro"); null while in trial or never subscribed. */
  planCode: string | null;
  hasSelectedPlan: boolean;
  emailConfirmed: boolean;
  isQualified: boolean;
  permissions: string[];
}
