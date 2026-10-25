import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api';
import type {
  CaseloadItem,
  Evidence,
  ManualCrisisResult,
  PatientFile,
  RiskLevel,
  SessionOutcome,
  StatementKey,
  SuggestedTherapist,
  TherapistDashboard,
  TriageCaseDetail,
  TriageDecisionRequest,
  TriageDecisionResult,
  TriageQueueItem,
} from './types';

/** Tamaneen.Api staff endpoints (docs/api-v1.md, Staff). */
@Injectable({ providedIn: 'root' })
export class StaffApiService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  private patient(pseudonymId: string): string {
    return `${this.base}/v1/staff/patients/${encodeURIComponent(pseudonymId)}`;
  }

  private triageCase(id: string): string {
    return `${this.base}/v1/staff/triage/${encodeURIComponent(id)}`;
  }

  dashboard(): Observable<TherapistDashboard> {
    return this.http.get<TherapistDashboard>(`${this.base}/v1/staff/dashboard`);
  }

  /** S06 /forbidden: records a page the route guards refused, in the audit ledger. */
  reportAccessDenied(path: string): Observable<void> {
    return this.http.post<void>(`${this.base}/v1/staff/access-denied`, { path });
  }

  recordOutcome(appointmentId: string, outcome: SessionOutcome): Observable<void> {
    return this.http.post<void>(`${this.base}/v1/staff/appointments/${encodeURIComponent(appointmentId)}/outcome`, { outcome });
  }

  // ---------- S02 clinical file ----------

  caseload(): Observable<CaseloadItem[]> {
    return this.http.get<CaseloadItem[]>(`${this.base}/v1/staff/patients`);
  }

  /** Audited on the server. */
  file(pseudonymId: string): Observable<PatientFile> {
    return this.http.get<PatientFile>(`${this.patient(pseudonymId)}/file`);
  }

  /** Source messages of one statement; audited. */
  evidence(pseudonymId: string, key: StatementKey): Observable<Evidence> {
    return this.http.get<Evidence>(`${this.patient(pseudonymId)}/evidence/${encodeURIComponent(key)}`);
  }

  /** 412 when the file changed since `version` was read. */
  approveFile(pseudonymId: string, version: string, edits: Partial<Record<StatementKey, string>>): Observable<PatientFile> {
    return this.http.post<PatientFile>(`${this.patient(pseudonymId)}/file/approve`, { edits }, { headers: new HttpHeaders({ 'If-Match': `"${version}"` }) });
  }

  dismissStatement(pseudonymId: string, statementKey: StatementKey, reason: string): Observable<PatientFile> {
    return this.http.post<PatientFile>(`${this.patient(pseudonymId)}/file/dismiss-item`, { statementKey, reason });
  }

  rateFile(pseudonymId: string, rating: number, comment?: string): Observable<void> {
    return this.http.post<void>(`${this.patient(pseudonymId)}/file-feedback`, { rating, comment });
  }

  raiseRisk(pseudonymId: string, riskLevel: RiskLevel, reason: string): Observable<{ riskLevel: RiskLevel }> {
    return this.http.post<{ riskLevel: RiskLevel }>(`${this.patient(pseudonymId)}/risk-flag`, { riskLevel, reason });
  }

  manualCrisis(pseudonymId: string, level: 3 | 4, note: string): Observable<ManualCrisisResult> {
    return this.http.post<ManualCrisisResult>(`${this.patient(pseudonymId)}/crisis`, { level, note });
  }

  // ---------- S03 triage ----------

  triageQueue(): Observable<TriageQueueItem[]> {
    return this.http.get<TriageQueueItem[]>(`${this.base}/v1/staff/triage`);
  }

  /** Audited on the server. */
  triageCaseDetail(id: string): Observable<TriageCaseDetail> {
    return this.http.get<TriageCaseDetail>(this.triageCase(id));
  }

  availableTherapists(id: string): Observable<SuggestedTherapist[]> {
    return this.http.get<SuggestedTherapist[]>(`${this.triageCase(id)}/therapists`);
  }

  /** 409 error.triage.claimed (with claimedBy, claimedUntil) when another supervisor holds it. */
  claim(id: string): Observable<TriageCaseDetail> {
    return this.http.post<TriageCaseDetail>(`${this.triageCase(id)}/claim`, {});
  }

  release(id: string): Observable<void> {
    return this.http.delete<void>(`${this.triageCase(id)}/claim`);
  }

  decide(id: string, request: TriageDecisionRequest): Observable<TriageDecisionResult> {
    return this.http.post<TriageDecisionResult>(`${this.triageCase(id)}/decisions`, request);
  }
}
