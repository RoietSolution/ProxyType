import { Component, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-portal-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './portal-layout.html',
  styleUrl: './portal-layout.scss',
})
export class PortalLayout {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly user = this.auth.user;
  readonly pageTitle = signal('Operations overview');
  readonly pageContext = signal('Platform / overview');

  constructor() {
    this.updatePageHeading(this.router.url);
    this.router.events.pipe(filter(event => event instanceof NavigationEnd)).subscribe(event => {
      this.updatePageHeading(event.urlAfterRedirects);
    });
  }

  logout(): void {
    this.auth.logout();
    location.assign('/login');
  }

  private updatePageHeading(url: string): void {
    const tree = this.router.parseUrl(url);
    const path = '/' + tree.root.children['primary']?.segments.map(segment => segment.path).join('/');
    const dashboardView = tree.queryParams['view'] || 'overview';
    const dashboardTitles: Record<string, string> = {
      overview: 'Operations overview', hierarchy: 'Organization hierarchy', services: 'Service permissions',
      wallet: 'Wallet balance', security: 'Security activity',
    };
    const titles: Record<string, string> = {
      '/charge-slabs': 'Charge Slabs', '/recharge-commissions': 'Recharge Commission',
      '/services/fino-dmt': 'Fino DMT', '/services/upi-transfer': 'UPI Transfer',
      '/services/aeps': 'AEPS', '/services/recharge': 'Recharge',
      '/services/wallet-to-wallet': 'Wallet to Wallet', '/services/fund-request': 'Wallet Top-up',
      '/transactions': 'Transactions', '/user-documents': 'User Documents', '/users': 'User Management',
      '/users/new': 'Create User',
    };
    const title = path === '/dashboard'
      ? (dashboardTitles[dashboardView] || dashboardTitles['overview'])
      : (titles[path] || (path.startsWith('/users/') ? 'User Details' : 'Workspace'));
    this.pageTitle.set(title);
    this.pageContext.set(path === '/dashboard' ? `Platform / ${dashboardView}` : `Platform / ${title}`);
  }
}
