import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { ActivatedRoute, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { DashboardData, EffectiveService, OrganizationUnit, ServiceDefinition, WalletBalance } from '../../core/models';
import { PortalApiService } from '../../core/portal-api.service';

type View = 'overview'|'hierarchy'|'services'|'wallet'|'security';
type ServiceTab = 'aeps'|'money-transfer'|'recharges'|'upcoming';

@Component({ selector: 'app-dashboard', imports: [CurrencyPipe, DatePipe, RouterLink, RouterLinkActive], templateUrl: './dashboard.html', styleUrl: './dashboard.scss' })
export class Dashboard implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly api = inject(PortalApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly user = this.auth.user;
  readonly view = signal<View>('overview');
  readonly loading = signal(true);
  readonly error = signal('');
  readonly dashboard = signal<DashboardData|null>(null);
  readonly walletBalance = signal<WalletBalance|null>(null);
  readonly walletLoading = signal(false);
  readonly organizations = signal<OrganizationUnit[]>([]);
  readonly serviceDefinitions = signal<ServiceDefinition[]>([]);
  readonly effectiveServices = signal<EffectiveService[]>([]);
  readonly selectedOrganizationId = signal('');
  readonly savingServiceId = signal('');
  readonly activeServiceTab = signal<ServiceTab>('aeps');
  readonly currentOrganization = computed(() => this.organizations().find(x => x.organizationUnitId === this.selectedOrganizationId()) ?? null);
  readonly serviceTabs: ReadonlyArray<{ key: ServiceTab; label: string }> = [
    { key: 'aeps', label: 'AEPS' },
    { key: 'money-transfer', label: 'Money Transfer' },
    { key: 'recharges', label: 'Recharges' },
    { key: 'upcoming', label: 'Upcoming' },
  ];
  readonly groupedDashboardServices = computed(() => {
    const groups: Record<ServiceTab, EffectiveService[]> = { aeps: [], 'money-transfer': [], recharges: [], upcoming: [] };
    for (const service of this.dashboard()?.services?.items || []) groups[this.serviceTabFor(service)].push(service);
    return groups;
  });
  readonly visibleDashboardServices = computed(() => this.groupedDashboardServices()[this.activeServiceTab()]);

  ngOnInit(): void {
    this.route.queryParamMap.subscribe(params => {
      const requested = params.get('view');
      const allowed: View[] = ['overview', 'hierarchy', 'services', 'wallet', 'security'];
      const nextView = allowed.includes(requested as View) ? requested as View : 'overview';
      this.view.set(nextView);
      if (nextView === 'wallet' && !this.walletBalance()) this.loadWalletBalance();
    });
    forkJoin({ dashboard: this.api.dashboard(), organizations: this.api.organizationTree(), services: this.api.services() }).subscribe({
      next: ({ dashboard, organizations, services }) => {
        this.dashboard.set(dashboard); this.organizations.set(organizations); this.serviceDefinitions.set(services);
        const selected = dashboard.organization?.organizationUnitId ?? organizations[0]?.organizationUnitId ?? '';
        this.selectedOrganizationId.set(selected); if (selected) this.loadPermissions(selected); this.loading.set(false);
      },
      error: () => { this.error.set('The dashboard could not be loaded. Please try again.'); this.loading.set(false); },
    });
  }
  selectView(view: View): void {
    this.view.set(view);
    void this.router.navigate([], { relativeTo: this.route, queryParams: { view }, queryParamsHandling: 'merge' });
  }
  loadWalletBalance(): void {
    this.walletLoading.set(true); this.error.set('');
    this.api.walletBalance().subscribe({
      next: balance => { this.walletBalance.set(balance); this.walletLoading.set(false); },
      error: () => { this.error.set('Wallet balances could not be loaded. Please try again.'); this.walletLoading.set(false); },
    });
  }
  selectServiceTab(tab: ServiceTab): void { this.activeServiceTab.set(tab); }
  selectOrganization(event: Event): void { const id = (event.target as HTMLSelectElement).value; this.selectedOrganizationId.set(id); this.loadPermissions(id); }
  changePermission(service: EffectiveService, effect: 'ALLOW'|'DENY'): void {
    const id = this.selectedOrganizationId(); if (!id) return; this.savingServiceId.set(service.serviceId);
    this.api.setPermission(id, service.serviceId, effect).subscribe({
      next: () => { this.loadPermissions(id); this.savingServiceId.set(''); },
      error: () => { this.error.set('Permission update failed. Verify your hierarchy role and the parent permission.'); this.savingServiceId.set(''); },
    });
  }
  logout(): void { this.auth.logout(); location.assign('/login'); }
  serviceRoute(code: string): string[] | null {
    if (code === 'fino_dmt') return ['/services/fino-dmt'];
    if (code === 'upi_transfer') return ['/services/upi-transfer'];
    if (code === 'aeps') return ['/services/aeps'];
    if (code.startsWith('recharge_')) return ['/services/recharge'];
    if (code === 'wallet_transfer') return ['/services/wallet-to-wallet'];
    if (code === 'fund_request') return ['/services/fund-request'];
    return null;
  }
  serviceIcon(code: string): string {
    if (code.startsWith('aeps')) return '◉';
    if (code.startsWith('dmt')) return '➤';
    if (code.startsWith('payout')) return '→';
    if (code.startsWith('recharge')) return '▯';
    if (code.startsWith('bbps')) return '▤';
    if (code.includes('pan')) return '▧';
    if (code.includes('account')) return '▥';
    if (code.includes('credit')) return '▣';
    if (code.includes('fund')) return '⌘';
    return '◇';
  }
  serviceIconPath(code: string): string {
    if (code.startsWith('aeps')) return '/icon-pack/AePS-Payment.jpeg';
    if (code.startsWith('dmt') || code === 'fino_dmt') return '/icon-pack/Money-Transfer.svg';
    if (code.startsWith('payout')) return '/icon-pack/Payout.svg';
    if (code.startsWith('recharge')) return code.includes('dth') ? '/icon-pack/DTH_Recharge.svg' : '/icon-pack/Mobile-Recharge.svg';
    if (code.startsWith('bbps') || code.includes('payment')) return '/icon-pack/Electricity-Bill.svg';
    if (code.includes('pan')) return '/icon-pack/PAN-NSDL.svg';
    if (code.includes('demat')) return '/icon-pack/Demat-Account.svg';
    if (code.includes('account')) return code.includes('business') ? '/icon-pack/Business-Account.svg' : '/icon-pack/Saving-Account.svg';
    if (code.includes('credit')) return '/icon-pack/Credit-Card.svg';
    if (code.includes('fund') || code.includes('wallet') || code.includes('upi')) return '/icon-pack/Wallet-Transfer.svg';
    if (code.includes('product')) return '/icon-pack/PRODUCTS.svg';
    return '/icon-pack/Other-Service.svg';
  }
  serviceTone(code: string): string {
    const tones = ['mint', 'blue', 'gold', 'pink', 'teal', 'violet'];
    return tones[Math.abs([...code].reduce((total, char) => total + char.charCodeAt(0), 0)) % tones.length];
  }
  isFeaturedService(service: EffectiveService): boolean {
    return /auth|aeps/i.test(`${service.code} ${service.name}`);
  }
  serviceTabFor(service: EffectiveService): ServiceTab {
    const text = `${service.code} ${service.name} ${service.category}`.toLowerCase();
    if (text.includes('aeps') || text.includes('aadhaar')) return 'aeps';
    if (text.includes('recharge') || text.includes('bill payment') || text.includes('bbps') || text.includes('credit card') || text.includes('dth')) return 'recharges';
    if (
      text.includes('dmt') || text.includes('transfer') || text.includes('payout') || text.includes('fund request') ||
      text.includes('cash deposit') || text.includes('move to bank') || text.includes('wallet') || text.includes('upi')
    ) return 'money-transfer';
    return 'upcoming';
  }
  private loadPermissions(id: string): void { this.api.effectiveServices(id).subscribe({ next: services => this.effectiveServices.set(services), error: () => this.effectiveServices.set([]) }); }
}
