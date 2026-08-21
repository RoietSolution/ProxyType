import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { PortalApiService } from '../../core/portal-api.service';
import { UserListItem } from '../../core/models';

@Component({ selector: 'app-user-details', imports: [ReactiveFormsModule], templateUrl: './user-details.html', styleUrl: './users.scss' })
export class UserDetails {
  private readonly api = inject(PortalApiService); private readonly route = inject(ActivatedRoute); private readonly router = inject(Router); private readonly builder = inject(FormBuilder);
  readonly user = signal<UserListItem|null>(null); readonly error = signal(''); readonly message = signal('');
  readonly password = this.builder.nonNullable.control('', [Validators.required, Validators.minLength(14)]);
  constructor() { const id = this.route.snapshot.paramMap.get('userId'); if (id) this.api.user(id).subscribe({ next: value => this.user.set(value), error: response => this.error.set(response?.error?.detail || 'Unable to load user details.') }); }
  resetPassword(): void { const item = this.user(); if (!item || this.password.invalid) { this.password.markAsTouched(); return; } this.api.resetUserPassword(item.userId, this.password.value).subscribe({ next: () => { this.message.set('Password reset saved. The user must change it at next login.'); this.password.reset(); }, error: response => this.error.set(response?.error?.detail || 'Unable to reset password.') }); }
  back(): void { void this.router.navigate(['/users']); }
}
