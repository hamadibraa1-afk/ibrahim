import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { API_BASE_URL, CRISIS_BASE_URL, isUnderBase } from '../api';
import { AuthService, SKIP_AUTH_RETRY } from './auth.service';

/**
 * Adds the staff access token to calls to Tamaneen.Api and the crisis worker, and nowhere else
 * (translation files, fonts and third parties never see it). On a 401 it refreshes once and retries once;
 * if that fails the session is over and the user goes back to sign-in.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const bases = [inject(API_BASE_URL), inject(CRISIS_BASE_URL)];
  if (!bases.some((base) => isUnderBase(request.url, base))) {
    return next(request);
  }
  const auth = inject(AuthService);
  const withToken = (req: HttpRequest<unknown>, token: string | null) => (token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req);

  return next(withToken(request, auth.accessToken())).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || request.context.get(SKIP_AUTH_RETRY)) {
        return throwError(() => error);
      }
      return from(auth.refresh()).pipe(
        switchMap((token) => {
          if (!token) {
            auth.expire();
            return throwError(() => error);
          }
          return next(withToken(request, token)).pipe(
            catchError((retryError: unknown) => {
              if (retryError instanceof HttpErrorResponse && retryError.status === 401) {
                auth.expire();
              }
              return throwError(() => retryError);
            }),
          );
        }),
      );
    }),
  );
};
