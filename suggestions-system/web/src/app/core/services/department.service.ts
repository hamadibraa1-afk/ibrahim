import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

/** إدارة من قائمة الإدارات التي يضيفها مدير النظام في الإعدادات. */
export interface Department {
  id: number;
  name: string;
  isActive: boolean;
  sortOrder: number;
  /** عدد المستخدمين المسجّلين في هذه الإدارة. */
  userCount: number;
}

@Injectable({ providedIn: 'root' })
export class DepartmentService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/departments`;

  getAll(activeOnly = false): Observable<Department[]> {
    return this.http.get<Department[]>(`${this.base}?activeOnly=${activeOnly}`);
  }
  create(name: string): Observable<Department> {
    return this.http.post<Department>(this.base, { name, isActive: true });
  }
  update(id: number, name: string, isActive: boolean): Observable<Department> {
    return this.http.put<Department>(`${this.base}/${id}`, { name, isActive });
  }
  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
