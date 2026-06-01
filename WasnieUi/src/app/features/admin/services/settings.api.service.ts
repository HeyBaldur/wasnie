import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface FieldRequirement {
  entityName: string;
  fieldName: string;
  isRequired: boolean;
}

@Injectable({ providedIn: 'root' })
export class SettingsApiService {
  private readonly http = inject(HttpClient);

  getFieldRequirements(): Observable<FieldRequirement[]> {
    return this.http.get<FieldRequirement[]>('/api/settings/field-requirements');
  }

  updateFieldRequirement(entityName: string, fieldName: string, isRequired: boolean): Observable<void> {
    return this.http.put<void>(
      `/api/settings/field-requirements/${entityName}/${fieldName}`,
      { isRequired }
    );
  }
}
