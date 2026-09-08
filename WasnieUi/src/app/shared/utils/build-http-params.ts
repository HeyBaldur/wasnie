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
  if (params.period) p = p.set('period', params.period);
  // ★ EVERY FIELD OF PaginationParams MUST BE FORWARDED HERE. A field added to the interface and
  //   forgotten here is a filter that vanishes in silence: the request goes out without it, the server
  //   quietly applies its own default, and the screen shows a window nobody asked for while nothing
  //   anywhere reports an error. That is exactly what happened to dateFrom/dateTo on their way in.
  if (params.dateFrom) p = p.set('dateFrom', params.dateFrom);
  if (params.dateTo) p = p.set('dateTo', params.dateTo);
  if (params.filters) {
    Object.entries(params.filters).forEach(([key, value]) => {
      p = p.set(key, value);
    });
  }
  return p;
}
