import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CRISIS_BASE_URL } from '../api';
import type { CloseCrisisRequest, CrisisActionRequest, CrisisEventDetail, CrisisHealth, Excerpt, HandoverRequest, OpenCrisisEvent, Responder, RevealRequest, RevealedContact } from './types';

/** Tamaneen.Crisis.Worker endpoints (docs/api-v1.md, Staff, "(worker)"). */
@Injectable({ providedIn: 'root' })
export class CrisisApiService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(CRISIS_BASE_URL);

  private event(id: string): string {
    return `${this.base}/v1/crisis/events/${encodeURIComponent(id)}`;
  }

  openEvents(): Observable<OpenCrisisEvent[]> {
    return this.http.get<OpenCrisisEvent[]>(`${this.base}/v1/crisis/events/open`);
  }

  detail(id: string): Observable<CrisisEventDetail> {
    return this.http.get<CrisisEventDetail>(this.event(id));
  }

  /** 409 when someone else acknowledged first. */
  acknowledge(id: string): Observable<OpenCrisisEvent> {
    return this.http.post<OpenCrisisEvent>(`${this.event(id)}/acknowledge`, {});
  }

  /** Step-up with the authenticator code; audited. */
  excerpt(id: string, totpCode: string): Observable<Excerpt> {
    return this.http.post<Excerpt>(`${this.event(id)}/excerpt`, { totpCode });
  }

  /** Step-up with the authenticator code and a reason; audited and shown for 10 minutes. */
  revealContact(id: string, request: RevealRequest): Observable<RevealedContact> {
    return this.http.post<RevealedContact>(`${this.event(id)}/reveal-contact`, request);
  }

  action(id: string, request: CrisisActionRequest): Observable<CrisisEventDetail> {
    return this.http.post<CrisisEventDetail>(`${this.event(id)}/actions`, request);
  }

  /** Closed, or still open when a supervisor's "unreachable" close waits for the clinical lead's co-sign. */
  close(id: string, request: CloseCrisisRequest): Observable<OpenCrisisEvent> {
    return this.http.post<OpenCrisisEvent>(`${this.event(id)}/close`, request);
  }

  /** Clinical lead: signs the waiting "unreachable" close. */
  coSignClose(id: string): Observable<OpenCrisisEvent> {
    return this.http.post<OpenCrisisEvent>(`${this.event(id)}/close/cosign`, {});
  }

  /** Colleagues who can take a handover. */
  responders(): Observable<Responder[]> {
    return this.http.get<Responder[]>(`${this.base}/v1/crisis/responders`);
  }

  /** Offers the case; the owner keeps it until the colleague accepts. */
  handover(id: string, request: HandoverRequest): Observable<CrisisEventDetail> {
    return this.http.post<CrisisEventDetail>(`${this.event(id)}/handover`, request);
  }

  acceptHandover(id: string): Observable<CrisisEventDetail> {
    return this.http.post<CrisisEventDetail>(`${this.event(id)}/handover/accept`, {});
  }

  /** The colleague declines, or the owner withdraws the offer. */
  declineHandover(id: string): Observable<CrisisEventDetail> {
    return this.http.post<CrisisEventDetail>(`${this.event(id)}/handover/decline`, {});
  }

  health(): Observable<CrisisHealth> {
    return this.http.get<CrisisHealth>(`${this.base}/v1/crisis/health`);
  }
}

/** Replaces or inserts a pushed or returned event; closed events leave the list. Newest deadline first is kept by the caller. */
export function upsertEvent<T extends OpenCrisisEvent>(events: readonly T[], next: T): T[] {
  const closed = next.status === 'Closed' || next.kind === 'closed';
  const index = events.findIndex((e) => e.eventId === next.eventId);
  if (closed) {
    return index === -1 ? [...events] : events.filter((e) => e.eventId !== next.eventId);
  }
  if (index === -1) {
    return [next, ...events];
  }
  const copy = [...events];
  copy[index] = { ...copy[index], ...next };
  return copy;
}
