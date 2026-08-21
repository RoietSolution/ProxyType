import { Routes } from '@angular/router';
import { authGuard, guestGuard, passwordChangeGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: 'login', canActivate: [guestGuard], loadComponent: () => import('./features/login/login').then(m => m.Login) },
  { path: 'change-password', canActivate: [authGuard], loadComponent: () => import('./features/change-password/change-password').then(m => m.ChangePassword) },
  { path: 'services/fino-dmt', canActivate: [passwordChangeGuard], loadComponent: () => import('./features/fino-dmt/fino-dmt').then(m => m.FinoDmt) },
  { path: 'services/upi-transfer', canActivate: [passwordChangeGuard], loadComponent: () => import('./features/upi-transfer/upi-transfer').then(m => m.UpiTransfer) },
  { path: 'services/aeps', canActivate: [passwordChangeGuard], loadComponent: () => import('./features/aeps/aeps').then(m => m.Aeps) },
  { path: 'services/recharge', canActivate: [passwordChangeGuard], loadComponent: () => import('./features/recharge/recharge').then(m => m.Recharge) },
  { path: 'services/wallet-to-wallet', canActivate: [passwordChangeGuard], loadComponent: () => import('./features/wallet-to-wallet/wallet-to-wallet').then(m => m.WalletToWallet) },
  { path: 'services/fund-request', canActivate: [passwordChangeGuard], loadComponent: () => import('./features/fund-request/fund-request').then(m => m.FundRequest) },
  { path: 'transactions', canActivate: [passwordChangeGuard], loadComponent: () => import('./features/transactions/transactions').then(m => m.Transactions) },
  { path: 'users', canActivate: [passwordChangeGuard], loadComponent: () => import('./features/users/users').then(m => m.Users) },
  { path: 'users/:userId', canActivate: [passwordChangeGuard], loadComponent: () => import('./features/users/user-details').then(m => m.UserDetails) },
  { path: 'dashboard', canActivate: [passwordChangeGuard], loadComponent: () => import('./features/dashboard/dashboard').then(m => m.Dashboard) },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: '**', redirectTo: 'dashboard' },
];
