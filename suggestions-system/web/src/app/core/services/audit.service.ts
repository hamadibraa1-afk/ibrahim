import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuditLog } from '../models/audit.model';

@Injectable({ providedIn: 'root' })
export class AuditService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/audit`;

  forProposal(proposalId: number): Observable<AuditLog[]> {
    return this.http.get<AuditLog[]>(`${this.base}/proposal/${proposalId}`);
  }

  all(filters: {
    search?: string; action?: string; actorId?: number;
    from?: string; to?: string; take?: number;
  } = {}): Observable<AuditLog[]> {
    let params = new HttpParams();
    Object.entries(filters).forEach(([k, v]) => {
      if (v !== undefined && v !== null && v !== '') params = params.set(k, String(v));
    });
    return this.http.get<AuditLog[]>(this.base, { params });
  }
}
