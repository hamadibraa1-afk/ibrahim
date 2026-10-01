import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * يرفق رمز الجلسة مع كل طلب، ويتعامل مع انتهاء الصلاحية (401) بإخراج
 * المستخدم مرة واحدة فقط مع حفظ الصفحة التي كان فيها للعودة إليها بعد الدخول.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.token;
  const request = token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(request).pipe(
    catchError((err: HttpErrorResponse) => {
      const isAuthCall = req.url.includes('/auth/login')
                      || req.url.includes('/auth/logout')
                      || req.url.includes('/auth/refresh');

      if (err.status === 401 && !isAuthCall && auth.isLoggedIn()) {
        const current = router.url;
        auth.clear();
        router.navigate(['/login'], {
          queryParams: {
            ...(current && !current.startsWith('/login') ? { redirect: current } : {}),
            expired: '1',
          },
        });
      }
      return throwError(() => err);
    })
  );
};
