import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { API_BASE_URL, CRISIS_BASE_URL, isUnderBase } from '../api';
import { LanguageService } from './language.service';

/**
 * Sends the portal language to Tamaneen.Api and the crisis worker so messages, labels and errors
 * come back in the language on screen.
 */
export const acceptLanguageInterceptor: HttpInterceptorFn = (request, next) => {
  const bases = [inject(API_BASE_URL), inject(CRISIS_BASE_URL)];
  if (!bases.some((base) => isUnderBase(request.url, base))) {
    return next(request);
  }
  const language = inject(LanguageService).language();
  return next(request.clone({ setHeaders: { 'Accept-Language': language } }));
};
