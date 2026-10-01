import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CreateUserRequest, UpdateUserRequest, User } from '../models/user.model';

@Injectable({ providedIn: 'root' })
export class UserService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/users`;

  getAll(): Observable<User[]> { return this.http.get<User[]>(this.base); }
  getDepartments(): Observable<string[]> { return this.http.get<string[]>(`${this.base}/departments`); }
  getById(id: number): Observable<User> { return this.http.get<User>(`${this.base}/${id}`); }
  create(payload: CreateUserRequest): Observable<User> { return this.http.post<User>(this.base, payload); }
  update(payload: UpdateUserRequest): Observable<User> { return this.http.put<User>(`${this.base}/${payload.id}`, payload); }
  suspend(id: number): Observable<User> { return this.http.post<User>(`${this.base}/${id}/suspend`, {}); }
  activate(id: number): Observable<User> { return this.http.post<User>(`${this.base}/${id}/activate`, {}); }
  delete(id: number): Observable<void> { return this.http.delete<void>(`${this.base}/${id}`); }
  resetPassword(id: number, newPassword: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/reset-password`, { newPassword });
  }
}
