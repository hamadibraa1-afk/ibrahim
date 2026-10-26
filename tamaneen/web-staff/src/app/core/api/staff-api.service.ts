import { HttpClient, HttpHeaders, type HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { API_BASE_URL } from '../api';
import type {
  AssignHomeworkRequest,
  ClosureQueueItem,
  ClosureView,
  Closure,
  HomeworkTemplate,
  PatientOverview,
  PlanHistory,
  PlanVersion,
  SafetyPlanVersion,
  SafetyPlanVersionSummary,
  SaveClosureRequest,
  SaveNoteRequest,
  SavePlanRequest,
  SessionNoteView,
  StaffHomework,
  StaffHomeworkList,
  StaffSafetyPlan,
  UpdateHomeworkRequest,
  UpdateSafetyPlanRequest,
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

  // ---------- Phase 5: care record ----------

  /** S14; audited. */
  overview(pseudonymId: string): Observable<PatientOverview> {
    return this.http.get<PatientOverview>(`${this.patient(pseudonymId)}/overview`);
  }

  homeworkTemplates(): Observable<HomeworkTemplate[]> {
    return this.http.get<HomeworkTemplate[]>(`${this.base}/v1/staff/homework/templates`);
  }

  homework(pseudonymId: string): Observable<StaffHomeworkList> {
    return this.http.get<StaffHomeworkList>(`${this.patient(pseudonymId)}/homework`);
  }

  assignHomework(pseudonymId: string, body: AssignHomeworkRequest): Observable<StaffHomework> {
    return this.http.post<StaffHomework>(`${this.patient(pseudonymId)}/homework`, body);
  }

  updateHomework(id: string, body: UpdateHomeworkRequest): Observable<StaffHomework> {
    return this.http.put<StaffHomework>(`${this.base}/v1/staff/homework/${encodeURIComponent(id)}`, body);
  }

  cancelHomework(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/v1/staff/homework/${encodeURIComponent(id)}`);
  }

  checkinFeedback(checkinId: string, text: string): Observable<void> {
    return this.http.post<void>(`${this.base}/v1/staff/checkins/${encodeURIComponent(checkinId)}/feedback`, { text });
  }

  /** The ETag is the current plan version ("0" before the first). */
  plans(pseudonymId: string): Observable<PlanHistory> {
    return this.http.get<PlanHistory>(`${this.patient(pseudonymId)}/plans`);
  }

  planVersion(pseudonymId: string, version: number): Observable<PlanVersion> {
    return this.http.get<PlanVersion>(`${this.patient(pseudonymId)}/plans/${version}`);
  }

  /** 412 when another version was saved after `basedOn`. */
  savePlan(pseudonymId: string, basedOn: number, body: SavePlanRequest): Observable<PlanVersion> {
    return this.http.post<PlanVersion>(`${this.patient(pseudonymId)}/plans`, body, { headers: ifMatch(basedOn) });
  }

  sessionNote(pseudonymId: string, appointmentId: string): Observable<SessionNoteView> {
    return this.http.get<SessionNoteView>(this.note(pseudonymId, appointmentId));
  }

  /** `revision` is the note's revision the draft is based on (0 before the first save). */
  saveNote(pseudonymId: string, appointmentId: string, revision: number, body: SaveNoteRequest): Observable<SessionNoteView> {
    return this.http.put<SessionNoteView>(this.note(pseudonymId, appointmentId), body, { headers: ifMatch(revision) });
  }

  signNote(pseudonymId: string, appointmentId: string, revision: number): Observable<SessionNoteView> {
    return this.http.post<SessionNoteView>(`${this.note(pseudonymId, appointmentId)}/sign`, {}, { headers: ifMatch(revision) });
  }

  addAddendum(pseudonymId: string, appointmentId: string, text: string): Observable<SessionNoteView> {
    return this.http.post<SessionNoteView>(`${this.note(pseudonymId, appointmentId)}/addenda`, { text });
  }

  safetyPlan(pseudonymId: string): Observable<StaffSafetyPlan> {
    return this.http.get<StaffSafetyPlan>(`${this.patient(pseudonymId)}/safety-plan`);
  }

  safetyPlanVersions(pseudonymId: string): Observable<SafetyPlanVersionSummary[]> {
    return this.http.get<SafetyPlanVersionSummary[]>(`${this.patient(pseudonymId)}/safety-plan/versions`);
  }

  safetyPlanVersion(pseudonymId: string, version: number): Observable<SafetyPlanVersion> {
    return this.http.get<SafetyPlanVersion>(`${this.patient(pseudonymId)}/safety-plan/versions/${version}`);
  }

  /** 412 when the patient (or a colleague) saved after `version`. */
  saveSafetyPlan(pseudonymId: string, version: number, body: UpdateSafetyPlanRequest): Observable<StaffSafetyPlan> {
    return this.http.post<StaffSafetyPlan>(`${this.patient(pseudonymId)}/safety-plans`, body, { headers: ifMatch(version) });
  }

  closure(pseudonymId: string): Observable<{ view: ClosureView; version: number }> {
    return this.http.get<ClosureView>(`${this.patient(pseudonymId)}/closure`, { observe: 'response' }).pipe(map(withVersion));
  }

  saveClosure(pseudonymId: string, version: number, body: SaveClosureRequest): Observable<{ view: ClosureView; version: number }> {
    return this.http.put<ClosureView>(`${this.patient(pseudonymId)}/closure`, body, { headers: ifMatch(version), observe: 'response' }).pipe(map(withVersion));
  }

  withdrawClosure(pseudonymId: string): Observable<{ view: ClosureView; version: number }> {
    return this.http.post<ClosureView>(`${this.patient(pseudonymId)}/closure/withdraw`, {}, { observe: 'response' }).pipe(map(withVersion));
  }

  closureQueue(): Observable<ClosureQueueItem[]> {
    return this.http.get<ClosureQueueItem[]>(`${this.base}/v1/staff/closures`);
  }

  signOffClosure(id: string, approve: boolean, reason?: string): Observable<Closure> {
    return this.http.post<Closure>(`${this.base}/v1/staff/closures/${encodeURIComponent(id)}/sign-off`, { approve, reason });
  }

  private note(pseudonymId: string, appointmentId: string): string {
    return `${this.patient(pseudonymId)}/sessions/${encodeURIComponent(appointmentId)}/note`;
  }
}

function ifMatch(version: number): HttpHeaders {
  return new HttpHeaders({ 'If-Match': `"${version}"` });
}

/** Reads the version from the ETag; without a readable header (CORS), from the body. */
function withVersion(response: HttpResponse<ClosureView>): { view: ClosureView; version: number } {
  const view = response.body!;
  const tag = Number((response.headers.get('ETag') ?? '').replace(/"/g, ''));
  const fallback = view.closure && view.closure.status !== 'signed_off' ? view.closure.revision : 0;
  return { view, version: Number.isFinite(tag) && response.headers.has('ETag') ? tag : fallback };
}
