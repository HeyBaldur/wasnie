import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  CategoryMapping,
  CreateCategoryMappingRequest,
  UpdateCategoryMappingRequest,
} from '../models/category-mapping.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';
import { buildHttpParams } from '../../../shared/utils/build-http-params';

@Injectable({ providedIn: 'root' })
export class CategoryMappingsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/category-mappings';

  list(params?: PaginationParams): Observable<PagedResult<CategoryMapping>> {
    return this.http.get<PagedResult<CategoryMapping>>(this.base, { params: buildHttpParams(params) });
  }

  create(request: CreateCategoryMappingRequest): Observable<CategoryMapping> {
    return this.http.post<CategoryMapping>(this.base, request);
  }

  update(id: string, request: UpdateCategoryMappingRequest): Observable<CategoryMapping> {
    return this.http.put<CategoryMapping>(`${this.base}/${id}`, { ...request, id });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
