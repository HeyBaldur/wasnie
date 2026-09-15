/**
 * The entity types the favorites system knows (KAN-64). Mirrors the backend `FavoriteEntityType`; the value is the
 * route segment of `/api/favorites/{type}`.
 *
 * ★ ADDING A SECTION: a value here, its entry in `FAVORITE_TYPES`, and the backend provider. Nothing else.
 */
export type FavoriteEntityType = 'payee' | 'plan';

/** One row of the quick-access table, as the server resolved it. A field the type does not have is null. */
export interface FavoriteItem {
  entityId: string;
  name: string;
  /** Payee: employee code. Plan: null. */
  code: string | null;
  /** Plan: version number. Payee: null. */
  version: number | null;
  /** The entity's status enum NAME ("Active", "Draft", …) — translated through a whitelist, never concatenated. */
  status: string;
}
