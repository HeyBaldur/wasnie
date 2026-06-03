import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Assignment, CreateAssignmentRequest } from '../models/assignment.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';
import { buildHttpParams } from '../../../shared/utils/build-http-params';

@Injectable({ providedIn: 'root' })
export class AssignmentsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/assignments';

  getAssignment(id: string): Observable<Assignment> {
    return this.http.get<Assignment>(`${this.base}/${id}`);
  }

  getAssignments(params?: PaginationParams): Observable<PagedResult<Assignment>> {
    return this.http.get<PagedResult<Assignment>>(this.base, { params: buildHttpParams(params) });
  }

  getAssignmentsByPlan(planId: string, params?: PaginationParams): Observable<PagedResult<Assignment>> {
    return this.http.get<PagedResult<Assignment>>(
      `${this.base}/plan/${planId}`, { params: buildHttpParams(params) });
  }

  getAssignmentsByPayee(payeeId: string, params?: PaginationParams): Observable<PagedResult<Assignment>> {
    return this.http.get<PagedResult<Assignment>>(
      `${this.base}/payee/${payeeId}`, { params: buildHttpParams(params) });
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
