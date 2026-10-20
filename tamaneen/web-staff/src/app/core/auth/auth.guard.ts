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

/** Crisis console: supervisors, clinical leads, and therapists on call. */
export const crisisAccessGuard: CanMatchFn = () => {
  const session = inject(SessionService);
  return session.crisisAccess() ? true : inject(Router).parseUrl(session.home());
};

/** Therapist dashboard. */
export const therapistGuard: CanMatchFn = () => {
  const session = inject(SessionService);
  return session.role() === 'therapist' ? true : inject(Router).parseUrl(session.home());
};
