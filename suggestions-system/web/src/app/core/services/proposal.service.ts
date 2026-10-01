import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AssignOwnerRequest, BlindReviewPolicy, CommitteeStudyRequest, CreateProposalRequest,
  ExecutiveDecisionRequest, ImpactReport, MeasureImpactRequest, Proposal, ProposalQuery,
  ReviewImpactRequest, ScreenDecisionRequest, UpdateProposalRequest,
} from '../models/proposal.model';

@Injectable({ providedIn: 'root' })
export class ProposalService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/proposals`;

  getAll(options: ProposalQuery = {}): Observable<Proposal[]> {
    let params = new HttpParams();
    Object.entries(options).forEach(([k, v]) => {
      if (v !== undefined && v !== null && v !== '') params = params.set(k, String(v));
    });
    return this.http.get<Proposal[]>(this.base, { params });
  }

  getById(id: number): Observable<Proposal> { return this.http.get<Proposal>(`${this.base}/${id}`); }
  create(payload: CreateProposalRequest): Observable<Proposal> { return this.http.post<Proposal>(this.base, payload); }
  update(id: number, payload: UpdateProposalRequest): Observable<Proposal> {
    return this.http.put<Proposal>(`${this.base}/${id}`, payload);
  }
  escalate(id: number, note: string): Observable<Proposal> {
    return this.http.post<Proposal>(`${this.base}/${id}/escalate`, { note });
  }
  delete(id: number): Observable<void> { return this.http.delete<void>(`${this.base}/${id}`); }

  screen(id: number, payload: ScreenDecisionRequest): Observable<Proposal> {
    return this.http.post<Proposal>(`${this.base}/${id}/screen`, payload);
  }
  saveCommitteeStudy(id: number, payload: CommitteeStudyRequest): Observable<Proposal> {
    return this.http.post<Proposal>(`${this.base}/${id}/committee-study`, payload);
  }
  committeeSign(id: number, memberId: number): Observable<Proposal> {
    return this.http.post<Proposal>(`${this.base}/${id}/committee-sign`, { memberId });
  }
  executiveDecision(id: number, payload: ExecutiveDecisionRequest): Observable<Proposal> {
    return this.http.post<Proposal>(`${this.base}/${id}/executive-decision`, payload);
  }
  assignOwner(id: number, payload: AssignOwnerRequest): Observable<Proposal> {
    return this.http.post<Proposal>(`${this.base}/${id}/assign-owner`, payload);
  }

  /** قاعدة كشف هوية مقدّم الطلب كما يطبّقها الخادم. */
  blindReviewPolicy(): Observable<BlindReviewPolicy> {
    return this.http.get<BlindReviewPolicy>(`${this.base}/blind-review-policy`);
  }

  // ---- قياس الأثر الفعلي بعد التطبيق ----
  measureImpact(id: number, payload: MeasureImpactRequest): Observable<Proposal> {
    return this.http.put<Proposal>(`${this.base}/${id}/impact`, payload);
  }
  reviewImpact(id: number, payload: ReviewImpactRequest): Observable<Proposal> {
    return this.http.post<Proposal>(`${this.base}/${id}/impact/review`, payload);
  }
  impactList(filters: { status?: string; overdue?: boolean } = {}): Observable<Proposal[]> {
    let params = new HttpParams();
    Object.entries(filters).forEach(([k, v]) => {
      if (v !== undefined && v !== null && v !== '') params = params.set(k, String(v));
    });
    return this.http.get<Proposal[]>(`${environment.apiUrl}/impact`, { params });
  }
  impactReport(): Observable<ImpactReport> {
    return this.http.get<ImpactReport>(`${environment.apiUrl}/impact/report`);
  }

  /** يفتح المستند في تبويب جديد عبر HttpClient حتى تمر كعكة الجلسة. */
  private openDoc(url: string): void {
    this.http.get(url, { responseType: 'blob' }).subscribe(blob => {
      const objectUrl = URL.createObjectURL(new Blob([blob], { type: 'text/html;charset=utf-8' }));
      window.open(objectUrl, '_blank');
      setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
    });
  }

  openOfficialForm(id: number): void { this.openDoc(`${this.base}/${id}/form-pdf`); }
  openMinutes(id: number): void { this.openDoc(`${this.base}/${id}/minutes`); }
  openCertificate(id: number): void { this.openDoc(`${this.base}/${id}/certificate`); }
}
