import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthState } from '../state/auth.state';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const auth = inject(AuthState);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status !== 401 || req.url.includes('/api/auth/')) {
        return throwError(() => err);
      }

      return auth.refresh$().pipe(
        switchMap(() => {
          const token = auth.getAccessToken();
          const retried = req.clone({
            setHeaders: { Authorization: `Bearer ${token}` },
          });
          return next(retried);
        }),
        catchError(() => {
          router.navigate(['/auth/login']);
          return throwError(() => err);
        }),
      );
    }),
  );
};
