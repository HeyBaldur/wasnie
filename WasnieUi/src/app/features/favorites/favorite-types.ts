import { BadgeVariant } from '../../shared/ui';
import { FavoriteEntityType } from './models/favorite.model';

/** How one status of one type is painted. */
export interface FavoriteStatusDisplay {
  key: string;
  variant: BadgeVariant;
  dot?: boolean;
}

/** Everything the quick-access table needs to know about one entity type — display only; no logic. */
export interface FavoriteTypeConfig {
  /** Route of the entity's detail page; the id is appended. */
  route: string;
  /** Title of the quick-access table. */
  titleKey: string;
  /**
   * Status enum NAME → how it is painted.
   *
   * ★ A WHITELIST, NOT A TEMPLATE (§C2). `'PLANS.STATUS_' + status` would print an internal identifier for any status
   * the client does not know yet. An unknown name falls back to {@link FALLBACK_STATUS}.
   */
  statuses: Record<string, FavoriteStatusDisplay>;
}

export const FALLBACK_STATUS: FavoriteStatusDisplay = { key: 'FAVORITES.STATUS_UNKNOWN', variant: 'neutral' };

/**
 * The per-section display config (KAN-64). Adding favorites to a new section is an entry here plus the backend
 * provider; the store, the star and the table are reused unchanged.
 *
 * The status keys and variants are the SAME ones the section's own list uses, so a favorite reads identically in the
 * quick-access table and in the list under it.
 */
export const FAVORITE_TYPES: Record<FavoriteEntityType, FavoriteTypeConfig> = {
  payee: {
    route: '/payees',
    titleKey: 'FAVORITES.TITLE_PAYEES',
    statuses: {
      Active: { key: 'PAYEES.STATUS_ACTIVE', variant: 'success', dot: true },
      OnLeave: { key: 'PAYEES.STATUS_ON_LEAVE', variant: 'warning' },
      Terminated: { key: 'PAYEES.STATUS_TERMINATED', variant: 'neutral' },
    },
  },
  plan: {
    route: '/plans',
    titleKey: 'FAVORITES.TITLE_PLANS',
    statuses: {
      Active: { key: 'PLANS.STATUS_ACTIVE', variant: 'success', dot: true },
      Draft: { key: 'PLANS.STATUS_DRAFT', variant: 'neutral' },
      Archived: { key: 'PLANS.STATUS_ARCHIVED', variant: 'neutral' },
    },
  },
};
