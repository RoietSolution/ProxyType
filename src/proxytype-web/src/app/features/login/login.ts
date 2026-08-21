import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth.service';

@Component({ selector: 'app-login', imports: [ReactiveFormsModule], templateUrl: './login.html', styleUrl: './login.scss' })
export class Login {
  private readonly formBuilder = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly revealPassword = signal(false);
  readonly form = this.formBuilder.nonNullable.group({
    username: ['', [Validators.required, Validators.minLength(3)]],
    password: ['', [Validators.required, Validators.minLength(8)]],
  });

  submit(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.loading.set(true); this.error.set('');
    this.auth.login(this.form.controls.username.value.trim(), this.form.controls.password.value)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: response => void this.router.navigate([response.user.mustChangePassword ? '/change-password' : '/dashboard']),
        error: () => this.error.set('We could not sign you in. Check your credentials or contact your administrator.'),
      });
  }
}
