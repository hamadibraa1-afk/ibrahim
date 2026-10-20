import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api';
import type { SessionOutcome, StaffIntakeView, TherapistDashboard } from './types';

/** Tamaneen.Api staff endpoints (docs/api-v1.md, Staff). */
@Injectable({ providedIn: 'root' })
export class StaffApiService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  dashboard(): Observable<TherapistDashboard> {
    return this.http.get<TherapistDashboard>(`${this.base}/v1/staff/dashboard`);
  }

  /** Audited on the server. */
  intake(pseudonymId: string): Observable<StaffIntakeView> {
    return this.http.get<StaffIntakeView>(`${this.base}/v1/staff/patients/${encodeURIComponent(pseudonymId)}/intake`);
  }

  approveIntake(pseudonymId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/v1/staff/patients/${encodeURIComponent(pseudonymId)}/intake/approve`, {});
  }

  recordOutcome(appointmentId: string, outcome: SessionOutcome): Observable<void> {
    return this.http.post<void>(`${this.base}/v1/staff/appointments/${encodeURIComponent(appointmentId)}/outcome`, { outcome });
  }
}
