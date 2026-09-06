import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { PricingRoleOption, RechargeCommission, UserListItem } from '../../core/models';
import { PortalApiService } from '../../core/portal-api.service';

@Component({ selector: 'app-recharge-commissions', imports: [FormsModule], templateUrl: './recharge-commissions.html', styleUrl: './recharge-commissions.scss' })
export class RechargeCommissions {
  private readonly api = inject(PortalApiService);
  private readonly router = inject(Router);
  readonly roles = signal<PricingRoleOption[]>([]);
  readonly cspUsers = signal<UserListItem[]>([]);
  readonly rows = signal<RechargeCommission[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly success = signal('');
  scopeMode: 'ROLE'|'USER' = 'ROLE';
  roleId = '';
  userId = '';
  userSearch = '';

  constructor() {
    this.api.rechargeCommissionRoles().subscribe({ next: roles => { this.roles.set(roles); this.roleId = roles.find(x => x.code === 'CSP_USER')?.roleId || roles[0]?.roleId || ''; if (this.roleId) this.load(); }, error: response => this.fail(response, 'Pricing roles could not be loaded.') });
    this.searchUsers();
  }
  load(): void {
    const scope = this.scope();
    if (!scope) { this.error.set(this.scopeMode === 'USER' ? 'Select a CSP user.' : 'Select a role.'); this.rows.set([]); return; }
    this.loading.set(true); this.error.set(''); this.success.set('');
    this.api.rechargeCommissions(scope).pipe(finalize(() => this.loading.set(false))).subscribe({ next: rows => this.rows.set(rows), error: response => this.fail(response, 'Recharge commissions could not be loaded.') });
  }
  changeScope(): void { this.rows.set([]); this.error.set(''); this.success.set(''); if (this.scope()) this.load(); }
  searchUsers(): void {
    this.api.users({ search: this.userSearch.trim() || undefined, active: true, unitType: 'CSP', page: 1, pageSize: 100 }).subscribe({
      next: page => { this.cspUsers.set(page.items); if (this.userId && !page.items.some(x => x.userId === this.userId)) this.userId = ''; },
      error: response => this.fail(response, 'CSP users could not be loaded.'),
    });
  }
  update(index: number, field: 'calculationType'|'rate', value: string): void { this.error.set(''); this.success.set(''); this.rows.update(rows => rows.map((row, i) => i !== index ? row : ({...row, isUserOverride: this.scopeMode === 'USER' ? true : row.isUserOverride, [field]: field === 'rate' ? Math.max(0, Number(value) || 0) : value} as RechargeCommission))); }
  setOverride(index: number, enabled: boolean): void { this.error.set(''); this.success.set(''); this.rows.update(rows => rows.map((row, i) => i === index ? {...row, isUserOverride: enabled} : row)); }
  save(): void {
    this.error.set(''); this.success.set('');
    const scope = this.scope();
    if (!scope) { this.error.set(this.scopeMode === 'USER' ? 'Select a CSP user before saving commissions.' : 'Select a role before saving commissions.'); return; }
    if (!this.rows().length) { this.error.set('There are no recharge operators to save.'); return; }
    if (this.rows().some(row => !Number.isFinite(Number(row.rate)) || Number(row.rate) < 0)) { this.error.set('Enter a valid non-negative rate for every operator.'); return; }
    this.saving.set(true);
    this.api.saveRechargeCommissions(scope, this.rows()).pipe(finalize(() => this.saving.set(false))).subscribe({ next: rows => { this.rows.set(rows); this.success.set(this.scopeMode === 'USER' ? 'CSP-specific commission overrides saved successfully.' : 'Recharge commissions saved successfully.'); }, error: response => this.fail(response, 'Recharge commissions could not be saved.') });
  }
  close(): void { void this.router.navigate(['/dashboard']); }
  private scope(): { roleId?: string; userId?: string } | null { return this.scopeMode === 'USER' ? (this.userId ? { userId: this.userId } : null) : (this.roleId ? { roleId: this.roleId } : null); }
  private fail(response: any, fallback: string): void { this.success.set(''); const validation = response?.error?.errors ? Object.values(response.error.errors).flat().join(' ') : ''; this.error.set(response?.error?.detail || validation || response?.error?.title || fallback); }
}
