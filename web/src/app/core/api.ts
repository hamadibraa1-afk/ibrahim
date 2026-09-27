import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppConfig } from './config';
import { I18n } from './i18n';

export interface ApiError { code: string; message: string; data?: any; }

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(I18n);

  get<T>(url: string, params?: Record<string, any>): Promise<T> {
    return firstValueFrom(this.http.get<T>(AppConfig.apiBase + '/api/' + url, { params: this.params(params) }));
  }
  post<T = void>(url: string, body: any = {}): Promise<T> { return firstValueFrom(this.http.post<T>(AppConfig.apiBase + '/api/' + url, body)); }
  put<T = void>(url: string, body: any): Promise<T> { return firstValueFrom(this.http.put<T>(AppConfig.apiBase + '/api/' + url, body)); }
  delete<T = void>(url: string): Promise<T> { return firstValueFrom(this.http.delete<T>(AppConfig.apiBase + '/api/' + url)); }
  /** A file as a Blob, through the same authenticated client as every other call. */
  blob(url: string): Promise<Blob> { return firstValueFrom(this.http.get(AppConfig.apiBase + '/api/' + url, { responseType: 'blob' })); }
  upload<T = void>(url: string, file: File, field = 'file'): Promise<T> {
    const form = new FormData();
    form.append(field, file, file.name);
    return firstValueFrom(this.http.post<T>(AppConfig.apiBase + '/api/' + url, form));
  }

  error(err: unknown): ApiError {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 0) return { code: 'network', message: this.i18n.t('err.network') };
      const body = err.error as Partial<ApiError> | null;
      if (body?.code) return { code: body.code, message: this.message(body.code, body.message), data: body.data };
      if (err.status === 400 && (err.error as any)?.errors) {
        const first = Object.values((err.error as any).errors as Record<string, string[]>)[0]?.[0];
        return { code: 'validation', message: first ?? this.i18n.t('err.generic') };
      }
    }
    return { code: 'unknown', message: this.i18n.t('err.generic') };
  }

  private message(code: string, fallback?: string): string {
    const key = 'err.' + code;
    return this.i18n.has(key) ? this.i18n.t(key) : (fallback ?? this.i18n.t('err.generic'));
  }

  private params(p?: Record<string, any>): HttpParams {
    let hp = new HttpParams();
    Object.entries(p ?? {}).forEach(([k, v]) => { if (v !== null && v !== undefined && v !== '') hp = hp.set(k, String(v)); });
    return hp;
  }
}
