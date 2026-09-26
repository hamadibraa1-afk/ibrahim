import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

export type Role = 'SystemAdmin' | 'Supervisor' | 'DepartmentManager' | 'Collector' | 'HrManager' | 'HrOfficer' | 'Employee';
export interface Session { token: string; expiresAt: string; userId: string; fullName: string; role: Role; language: 'ar' | 'en'; }

const KEY = 'session';
const FIELD_OFFICE: Role[] = ['SystemAdmin', 'Supervisor', 'DepartmentManager'];
const HR_OFFICE: Role[] = ['SystemAdmin', 'HrManager', 'HrOfficer', 'DepartmentManager'];
const SELF_SERVICE: Role[] = ['Collector', 'Employee'];
const OFFICE_SELF: Role[] = ['Employee'];

@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly router = inject(Router);
  readonly session = signal<Session | null>(this.load());
  readonly role = computed<Role | null>(() => this.session()?.role ?? null);

  /** Operational changes in the field module: administrators and supervisors. */
  readonly canManage = computed(() => this.role() === 'SystemAdmin' || this.role() === 'Supervisor');
  /** Accounts and roles: administrators only. */
  readonly canAdmin = computed(() => this.role() === 'SystemAdmin');
  /** HR data changes: administrators and HR managers. HR officers and department managers review. */
  readonly canManageHr = computed(() => this.role() === 'SystemAdmin' || this.role() === 'HrManager');

  readonly canField = computed(() => FIELD_OFFICE.includes(this.role()!));
  readonly canHr = computed(() => HR_OFFICE.includes(this.role()!));

  set(s: Session): void { localStorage.setItem(KEY, JSON.stringify(s)); this.session.set(s); }

  logout(): void {
    localStorage.removeItem(KEY);
    this.session.set(null);
    this.router.navigateByUrl('/login');
  }

  /** Where to land after signing in: the picker only appears when both modules are available. */
  home(): string {
    const role = this.role();
    if (!role) return '/login';
    if (role === 'Employee') return '/my';
    if (SELF_SERVICE.includes(role)) return '/me';
    if (this.canField() && this.canHr()) return '/select';
    return this.canHr() ? '/hr' : '/admin';
  }

  private load(): Session | null {
    try {
      const s = JSON.parse(localStorage.getItem(KEY) ?? 'null') as Session | null;
      return s && new Date(s.expiresAt) > new Date() ? s : null;
    } catch { return null; }
  }
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(Auth);
  const token = auth.session()?.token;
  const isApi = req.url.includes('/api/') && !req.url.includes('/api/public/');
  const request = token && isApi ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
  return next(request).pipe(catchError((err: unknown) => {
    if (err instanceof HttpErrorResponse && err.status === 401 && !req.url.endsWith('/auth/login')) auth.logout();
    return throwError(() => err);
  }));
};

const guard = (allowed: Role[]): CanActivateFn => () => {
  const auth = inject(Auth);
  const router = inject(Router);
  if (!auth.session()) return router.parseUrl('/login');
  return allowed.includes(auth.role()!) ? true : router.parseUrl(auth.home());
};

export const officeGuard = guard(FIELD_OFFICE);
export const hrGuard = guard(HR_OFFICE);
export const employeeGuard = guard([...OFFICE_SELF, 'HrManager', 'HrOfficer', 'DepartmentManager', 'SystemAdmin']);
export const signedInGuard: CanActivateFn = () => {
  const auth = inject(Auth);
  const router = inject(Router);
  return auth.session() ? true : router.parseUrl('/login');
};
