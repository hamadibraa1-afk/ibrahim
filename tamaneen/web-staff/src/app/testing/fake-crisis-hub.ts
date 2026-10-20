import { DestroyRef, Injectable, signal } from '@angular/core';
import { Subject } from 'rxjs';
import type { OpenCrisisEvent } from '../core/api/types';
import type { HubStatus } from '../core/realtime/crisis-hub.service';

/** Stand-in for CrisisHubService in component tests: push alerts with alerts$.next(). */
@Injectable()
export class FakeCrisisHub {
  readonly alertsSubject = new Subject<OpenCrisisEvent>();
  readonly reconnectedSubject = new Subject<void>();
  readonly alerts = this.alertsSubject.asObservable();
  readonly reconnected = this.reconnectedSubject.asObservable();
  readonly status = signal<HubStatus>('connected');
  users = 0;

  acquire(destroyRef?: DestroyRef): () => void {
    this.users++;
    const release = () => {
      this.users--;
    };
    destroyRef?.onDestroy(release);
    return release;
  }
}
