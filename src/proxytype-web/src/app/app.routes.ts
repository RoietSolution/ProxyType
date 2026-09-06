import { Routes } from '@angular/router';
import { authGuard, guestGuard, passwordChangeGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: 'login', canActivate: [guestGuard], loadComponent: () => import('./features/login/login').then(m => m.Login) },
  { path: 'change-password', canActivate: [authGuard], loadComponent: () => import('./features/change-password/change-password').then(m => m.ChangePassword) },
  {
    path: '',
    canActivate: [passwordChangeGuard],
    loadComponent: () => import('./layout/portal-layout').then(m => m.PortalLayout),
    children: [
      { path: 'services/fino-dmt', loadComponent: () => import('./features/fino-dmt/fino-dmt').then(m => m.FinoDmt) },
      { path: 'services/upi-transfer', loadComponent: () => import('./features/upi-transfer/upi-transfer').then(m => m.UpiTransfer) },
      { path: 'services/aeps', loadComponent: () => import('./features/aeps/aeps').then(m => m.Aeps) },
      { path: 'services/recharge', loadComponent: () => import('./features/recharge/recharge').then(m => m.Recharge) },
      { path: 'services/wallet-to-wallet', loadComponent: () => import('./features/wallet-to-wallet/wallet-to-wallet').then(m => m.WalletToWallet) },
      { path: 'services/fund-request', loadComponent: () => import('./features/fund-request/fund-request').then(m => m.FundRequest) },
      { path: 'charge-slabs', loadComponent: () => import('./features/charge-slabs/charge-slabs').then(m => m.ChargeSlabs) },
      { path: 'recharge-commissions', loadComponent: () => import('./features/recharge-commissions/recharge-commissions').then(m => m.RechargeCommissions) },
      { path: 'transactions', loadComponent: () => import('./features/transactions/transactions').then(m => m.Transactions) },
      { path: 'user-documents', loadComponent: () => import('./features/user-documents/user-documents').then(m => m.UserDocuments) },
      { path: 'users', loadComponent: () => import('./features/users/users').then(m => m.Users) },
      { path: 'users/new', loadComponent: () => import('./features/users/user-form').then(m => m.UserForm) },
      { path: 'users/:userId/edit', loadComponent: () => import('./features/users/user-form').then(m => m.UserForm) },
      { path: 'users/:userId', loadComponent: () => import('./features/users/user-details').then(m => m.UserDetails) },
      { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard').then(m => m.Dashboard) },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
    ],
  },
  { path: '**', redirectTo: 'dashboard' },
];
