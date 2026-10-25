import { inject } from '@angular/core';
import { CanActivateFn, CanMatchFn, Router } from '@angular/router';
import { SessionService } from '../session/session.service';
import { AuthService } from './auth.service';

/** Every portal page needs a signed-in staff member; others go to sign-in and come back afterwards. */
export const authGuard: CanActivateFn = (_route, state) => {
  if (inject(AuthService).signedIn()) {
    return true;
  }
  const returnUrl = state.url && state.url !== '/' ? state.url : undefined;
  return inject(Router).createUrlTree(['/login'], { queryParams: returnUrl ? { returnUrl } : {} });
};

/** The sign-in page is only for people who are not signed in yet. */
export const signedOutGuard: CanActivateFn = () => {
  if (!inject(AuthService).signedIn()) {
    return true;
  }
  return inject(Router).parseUrl(inject(SessionService).home());
};

/**
 * Crisis console: supervisors, clinical leads and on-call therapists work it from the menu. A therapist who is
 * not on call still reaches it from an alert on her dashboard, to work her own patient's event (the worker only
 * lists the events she may see).
 */
export const crisisAccessGuard: CanMatchFn = () => {
  const session = inject(SessionService);
  // Matching runs before the shell's authGuard: leave signed-out visitors to it so the deep link is kept.
  const allowed = session.crisisAccess() || session.role() === 'therapist';
  return !inject(AuthService).signedIn() || allowed ? true : inject(Router).parseUrl(session.home());
};

/** Patient files (S02) and "My patients": therapists, supervisors and the clinical lead. The server limits a therapist to her patients. */
export const clinicalGuard: CanMatchFn = () => {
  const session = inject(SessionService);
  const role = session.role();
  return !inject(AuthService).signedIn() || role === 'therapist' || role === 'supervisor' || role === 'lead' ? true : inject(Router).parseUrl(session.home());
};

/** Triage review (S03): supervisors and the clinical lead. */
export const triageGuard: CanMatchFn = () => {
  const session = inject(SessionService);
  const role = session.role();
  return !inject(AuthService).signedIn() || role === 'supervisor' || role === 'lead' ? true : inject(Router).parseUrl(session.home());
};

/** Therapist dashboard. */
export const therapistGuard: CanMatchFn = () => {
  const session = inject(SessionService);
  return !inject(AuthService).signedIn() || session.role() === 'therapist' ? true : inject(Router).parseUrl(session.home());
};
