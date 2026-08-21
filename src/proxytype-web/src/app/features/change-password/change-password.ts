import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth.service';

@Component({ selector: 'app-change-password', imports: [ReactiveFormsModule], templateUrl: './change-password.html', styleUrl: './change-password.scss' })
export class ChangePassword {
  private readonly builder = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly form = this.builder.nonNullable.group({
    currentPassword: ['', [Validators.required, Validators.minLength(8)]],
    newPassword: ['', [Validators.required, Validators.minLength(14)]],
    confirmPassword: ['', Validators.required],
  });
  submit(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const value = this.form.getRawValue();
    if (value.newPassword !== value.confirmPassword) { this.error.set('The new password and confirmation do not match.'); return; }
    this.loading.set(true); this.error.set('');
    this.auth.changePassword(value.currentPassword, value.newPassword, value.confirmPassword).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: () => { this.auth.clear(); void this.router.navigate(['/login']); },
      error: () => this.error.set('The password could not be changed. Verify your current password and try again.'),
    });
  }
}
