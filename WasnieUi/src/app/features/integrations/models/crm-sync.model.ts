// Phase 2 CRM deal-sync models (HubSpot). Read-only against HubSpot; these describe Wasnie-side results.

/** One closed-won deal previewed from HubSpot (FASE 2a) — nothing is created. */
export interface HubSpotDealPreviewItem {
  dealId: string;
  dealName: string | null;
  amount: number | null;
  currencyCode: string | null;
  closeDate: string | null;
  ownerId: string | null;
  ownerName: string | null;
  ownerEmail: string | null;
}

export interface HubSpotDealsPreview {
  count: number;
  deals: HubSpotDealPreviewItem[];
}

/** Summary of a deal import (FASE 2c). */
export interface HubSpotImportResult {
  dealsRead: number;
  created: number;
  assignedToPayee: number;
  unassigned: number;
  skippedAlreadyImported: number;
  skippedInvalid: number;
  newOwnerMappings: number;
  warnings: string[];
}

/** A HubSpot owner awaiting a manual link to a payee (FASE 2d). */
export interface UnresolvedCrmOwner {
  ownerId: string;
  name: string | null;
  email: string | null;
  archived: boolean;
  closedWonDealCount: number;
  unassignedTransactionCount: number;
}

export interface UnresolvedCrmOwners {
  count: number;
  owners: UnresolvedCrmOwner[];
}

export interface LinkCrmOwnerRequest {
  ownerId: string;
  payeeId: string;
  reassignExistingUnassigned: boolean;
}

export interface LinkCrmOwnerResult {
  reassignedTransactions: number;
}
