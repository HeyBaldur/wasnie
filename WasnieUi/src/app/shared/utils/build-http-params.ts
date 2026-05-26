import { HttpParams } from '@angular/common/http';
import { PaginationParams } from '../models/pagination.models';

export function buildHttpParams(params?: PaginationParams): HttpParams {
  let p = new HttpParams();
  if (!params) return p;

  p = p.set('page', String(params.page));
  p = p.set('pageSize', String(params.pageSize));
  if (params.search) p = p.set('search', params.search);
  if (params.sortBy) p = p.set('sortBy', params.sortBy);
  if (params.sortOrder) p = p.set('sortOrder', params.sortOrder);
  if (params.filters) {
    Object.entries(params.filters).forEach(([key, value]) => {
      p = p.set(key, value);
    });
  }
  return p;
}
