import { HttpErrorResponse, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, tap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * الجلسة محفوظة في كعكة HttpOnly لا تستطيع الواجهة قراءتها؛ هنا نطلب من المتصفح إرسالها
 * فقط، ونزامن موعد انتهاء الجلسة المنزلقة من ترويسة X-Session-Expires التي يعيدها الخادم.
 * ونتعامل مع انتهاء الصلاحية (401) بإخراج المستخدم مرة واحدة مع حفظ الصفحة للعودة إليها.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(req.clone({ withCredentials: true })).pipe(
    tap(event => {
      if (event instanceof HttpResponse) {
        const expires = event.headers.get('X-Session-Expires');
        if (expires) auth.syncExpiry(expires);
      }
    }),
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
