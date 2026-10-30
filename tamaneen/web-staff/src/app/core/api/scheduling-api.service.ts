import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api';
import type {
  Availability,
  FreeSlot,
  AvailabilityPreview,
  AvailabilitySlot,
  SaveAvailabilityRequest,
  SaveAvailabilityResult,
  Schedule,
  StaffAppointment,
  TherapistCancelReason,
} from './types';

/** S18 schedule and S19 availability (docs/api-v1.md, Staff). Recording an outcome stays on StaffApiService. */
@Injectable({ providedIn: 'root' })
export class SchedulingApiService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  private appointment(id: string): string {
    return `${this.base}/v1/staff/appointments/${encodeURIComponent(id)}`;
  }

  /** `from` is a local date (yyyy-MM-dd); days 1 (day view) or 7 (week view). Audited. */
  schedule(from: string, days: number): Observable<Schedule> {
    return this.http.get<Schedule>(`${this.base}/v1/staff/schedule`, { params: new HttpParams().set('from', from).set('days', days) });
  }

  runningLate(id: string, minutes: number): Observable<StaffAppointment> {
    return this.http.post<StaffAppointment>(`${this.appointment(id)}/running-late`, { minutes });
  }

  cancel(id: string, reasonCode: TherapistCancelReason, note?: string): Observable<StaffAppointment> {
    return this.http.post<StaffAppointment>(`${this.appointment(id)}/cancel`, { reasonCode, note: note || undefined });
  }

  /** SB-26: moves the session to another of the therapist's own free times; the reason is required, the patient is told. */
  reschedule(id: string, slotId: string, reasonCode: TherapistCancelReason): Observable<StaffAppointment> {
    return this.http.post<StaffAppointment>(`${this.appointment(id)}/reschedule`, { slotId, reasonCode });
  }

  /** The therapist's own free times from now on (default 28 days). */
  freeSlots(days = 28): Observable<FreeSlot[]> {
    return this.http.get<FreeSlot[]>(`${this.base}/v1/staff/free-slots`, { params: new HttpParams().set('days', days) });
  }

  availability(): Observable<Availability> {
    return this.http.get<Availability>(`${this.base}/v1/staff/availability`);
  }

  preview(request: SaveAvailabilityRequest): Observable<AvailabilityPreview> {
    return this.http.post<AvailabilityPreview>(`${this.base}/v1/staff/availability/preview`, request);
  }

  save(request: SaveAvailabilityRequest): Observable<SaveAvailabilityResult> {
    return this.http.put<SaveAvailabilityResult>(`${this.base}/v1/staff/availability`, request);
  }

  addSlot(startsAt: string): Observable<AvailabilitySlot> {
    return this.http.post<AvailabilitySlot>(`${this.base}/v1/staff/availability/slots`, { startsAt });
  }

  moveSlot(slotId: string, startsAt: string): Observable<AvailabilitySlot> {
    return this.http.patch<AvailabilitySlot>(`${this.base}/v1/staff/availability/slots/${encodeURIComponent(slotId)}`, { startsAt });
  }

  removeSlot(slotId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/v1/staff/availability/slots/${encodeURIComponent(slotId)}`);
  }
}
