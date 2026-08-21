import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const token = auth.accessToken();
  const authenticated = token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
  return next(authenticated).pipe(catchError((error: HttpErrorResponse) => {
    if (error.status !== 401 || request.url.includes('/api/auth/') || !auth.isAuthenticated()) return throwError(() => error);
    return auth.refresh().pipe(
      switchMap(() => next(request.clone({ setHeaders: { Authorization: `Bearer ${auth.accessToken()}` } }))),
      catchError(refreshError => { auth.clear(); void router.navigate(['/login']); return throwError(() => refreshError); }),
    );
  }));
};
