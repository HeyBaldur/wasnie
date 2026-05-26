import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Assignment, CreateAssignmentRequest } from '../models/assignment.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

@Injectable({ providedIn: 'root' })
export class AssignmentsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/assignments';

  getAssignments(params?: PaginationParams): Observable<PagedResult<Assignment>> {
    let httpParams = new HttpParams();
    if (params) {
      httpParams = httpParams.set('page', String(params.page));
      httpParams = httpParams.set('pageSize', String(params.pageSize));
      if (params.search) httpParams = httpParams.set('search', params.search);
      if (params.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
      if (params.sortOrder) httpParams = httpParams.set('sortOrder', params.sortOrder);
      if (params.filters) {
        Object.entries(params.filters).forEach(([key, value]) => {
          httpParams = httpParams.set(`filters[${key}]`, value);
        });
      }
    }
    return this.http.get<PagedResult<Assignment>>(this.base, { params: httpParams });
  }

  getAssignmentsByPlan(planId: string, params?: PaginationParams): Observable<PagedResult<Assignment>> {
    let httpParams = new HttpParams();
    if (params) {
      httpParams = httpParams.set('page', String(params.page));
      httpParams = httpParams.set('pageSize', String(params.pageSize));
      if (params.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
      if (params.sortOrder) httpParams = httpParams.set('sortOrder', params.sortOrder);
      if (params.filters) {
        Object.entries(params.filters).forEach(([key, value]) => {
          httpParams = httpParams.set(`filters[${key}]`, value);
        });
      }
    }
    return this.http.get<PagedResult<Assignment>>(`${this.base}/plan/${planId}`, { params: httpParams });
  }

  getAssignmentsByPayee(payeeId: string, params?: PaginationParams): Observable<PagedResult<Assignment>> {
    let httpParams = new HttpParams();
    if (params) {
      httpParams = httpParams.set('page', String(params.page));
      httpParams = httpParams.set('pageSize', String(params.pageSize));
      if (params.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
      if (params.sortOrder) httpParams = httpParams.set('sortOrder', params.sortOrder);
      if (params.filters) {
        Object.entries(params.filters).forEach(([key, value]) => {
          httpParams = httpParams.set(`filters[${key}]`, value);
        });
      }
    }
    return this.http.get<PagedResult<Assignment>>(`${this.base}/payee/${payeeId}`, { params: httpParams });
  }

  createAssignment(request: CreateAssignmentRequest): Observable<Assignment> {
    return this.http.post<Assignment>(this.base, request);
  }

  updateNotes(assignmentId: string, notes: string | null): Observable<void> {
    return this.http.put<void>(`${this.base}/${assignmentId}/notes`, { notes });
  }

  deactivateAssignment(assignmentId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${assignmentId}/deactivate`, {});
  }
}
