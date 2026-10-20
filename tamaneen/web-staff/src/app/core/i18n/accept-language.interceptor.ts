import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { API_BASE_URL } from '../api';
import { LanguageService } from './language.service';

/** Sends the portal language to Tamaneen.Api so messages and errors come back in the language on screen. */
export const acceptLanguageInterceptor: HttpInterceptorFn = (request, next) => {
  const apiBase = inject(API_BASE_URL);
  if (!isApiRequest(request.url, apiBase)) {
    return next(request);
  }
  const language = inject(LanguageService).language();
  return next(request.clone({ setHeaders: { 'Accept-Language': language } }));
};

function isApiRequest(url: string, apiBase: string): boolean {
  const base = apiBase.endsWith('/') ? apiBase : `${apiBase}/`;
  return url === apiBase || url.startsWith(base);
}
