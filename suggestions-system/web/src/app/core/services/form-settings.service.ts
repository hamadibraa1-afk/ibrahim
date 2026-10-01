import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { FormField, SaveFormFieldRequest } from '../models/form-field.model';

@Injectable({ providedIn: 'root' })
export class FormSettingsService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/form-settings`;

  getAll(activeOnly = false): Observable<FormField[]> {
    return this.http.get<FormField[]>(`${this.base}?activeOnly=${activeOnly}`);
  }
  create(payload: SaveFormFieldRequest): Observable<FormField> {
    return this.http.post<FormField>(this.base, payload);
  }
  update(id: number, payload: SaveFormFieldRequest): Observable<FormField> {
    return this.http.put<FormField>(`${this.base}/${id}`, payload);
  }
  reorder(orderedIds: number[]): Observable<void> {
    return this.http.post<void>(`${this.base}/reorder`, { orderedIds });
  }
  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
