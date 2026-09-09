export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
  /** Tenant-wide unfiltered total — only populated by endpoints that support advanced filtering. */
  unfilteredTotal?: number;
}

export interface PaginationParams {
  page: number;
  pageSize: number;
  sortBy?: string;
  sortOrder?: 'asc' | 'desc';
  search?: string;
  filters?: Record<string, string>;
  /** Dashboard period selector value — passed to backend PaginationQuery.Period. */
  period?: string;
  /**
   * An explicit [from, to] window, ISO yyyy-MM-dd. Takes precedence over `period` on the server
   * (see ListAssignmentsByPayeeHandler.ResolveExplicitRange), which is what lets a screen that has
   * moved to a free date range keep using these list endpoints.
   */
  dateFrom?: string;
  dateTo?: string;
}
