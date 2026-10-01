import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { UserRole } from '../models/user.model';

/** يمنع الوصول لأي صفحة دون تسجيل دخول. */
export const authGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  // جلسة محفوظة لكنها منتهية تُعامَل كغير مسجَّلة
  if (auth.isLoggedIn() && auth.isExpired()) auth.clear();

  if (auth.isLoggedIn()) return true;
  return router.createUrlTree(['/login'], { queryParams: { redirect: state.url } });
};

/** يقصر الصفحة على أدوار محددة — تُمرَّر عبر route.data.roles */
export const roleGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const allowed = (route.data?.['roles'] as UserRole[]) ?? [];
  const role = auth.role();

  if (role && allowed.includes(role)) return true;
  return router.createUrlTree(['/my-proposals']);
};

/** يمنع فتح صفحة الدخول لمستخدم مسجَّل بالفعل. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isLoggedIn() && auth.isExpired()) auth.clear();
  return auth.isLoggedIn() ? router.createUrlTree(['/my-proposals']) : true;
};
