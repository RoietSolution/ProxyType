import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () => inject(AuthService).isAuthenticated() ? true : inject(Router).createUrlTree(['/login']);
export const guestGuard: CanActivateFn = () => inject(AuthService).isAuthenticated() ? inject(Router).createUrlTree(['/dashboard']) : true;
export const passwordChangeGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (!auth.isAuthenticated()) return inject(Router).createUrlTree(['/login']);
  return auth.user()?.mustChangePassword ? inject(Router).createUrlTree(['/change-password']) : true;
};
