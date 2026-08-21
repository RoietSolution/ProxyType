import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { PortalApiService } from '../../core/portal-api.service';
import { UserListItem } from '../../core/models';

@Component({ selector: 'app-users', imports: [ReactiveFormsModule, RouterLink], templateUrl: './users.html', styleUrl: './users.scss' })
export class Users {
  private readonly api = inject(PortalApiService); private readonly builder = inject(FormBuilder); private readonly route = inject(ActivatedRoute);
  readonly users = signal<UserListItem[]>([]); readonly loading = signal(false); readonly error = signal(''); readonly page = signal(1); readonly pageSize = signal(50); readonly total = signal(0); readonly totalPages = signal(1); readonly unitType = signal('');
  readonly search = this.builder.nonNullable.control(''); readonly active = this.builder.nonNullable.control('');
  constructor() { this.route.queryParamMap.subscribe(params => { this.unitType.set((params.get('type') || '').toUpperCase()); this.page.set(1); this.load(); }); }
  title(): string { return this.unitType() ? `${this.unitType()} List` : 'User Base'; }
  load(): void { this.loading.set(true); this.error.set(''); const value = this.active.value === '' ? undefined : this.active.value === 'true'; this.api.users({ search: this.search.value, active: value, unitType: this.unitType() || undefined, page: this.page(), pageSize: this.pageSize() }).pipe(finalize(() => this.loading.set(false))).subscribe({ next: response => { this.users.set(response.items); this.total.set(response.total); this.totalPages.set(Math.max(1, response.totalPages)); }, error: response => this.error.set(response?.error?.detail || 'Unable to load users.') }); }
  changePageSize(event: Event): void { this.pageSize.set(Number((event.target as HTMLSelectElement).value)); this.page.set(1); this.load(); }
  previous(): void { if (this.page() > 1) { this.page.update(value => value - 1); this.load(); } }
  next(): void { if (this.page() < this.totalPages()) { this.page.update(value => value + 1); this.load(); } }
  disable(user: UserListItem): void { if (!confirm(`Disable ${user.displayName}?`)) return; this.api.disableUser(user.userId).subscribe({ next: () => this.load(), error: response => this.error.set(response?.error?.detail || 'Unable to disable user.') }); }
}
