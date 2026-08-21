import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { FinoDmtResponse } from '../../core/models';
import { PortalApiService } from '../../core/portal-api.service';

@Component({ selector: 'app-fino-dmt', imports: [ReactiveFormsModule], templateUrl: './fino-dmt.html', styleUrl: './fino-dmt.scss' })
export class FinoDmt {
  private readonly builder = inject(FormBuilder);
  private readonly api = inject(PortalApiService);
  private readonly router = inject(Router);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly result = signal<FinoDmtResponse|null>(null);
  readonly form = this.builder.nonNullable.group({
    accountNumber: ['', [Validators.required, Validators.pattern(/^\d{9,18}$/)]],
    ifscCode: ['', [Validators.required, Validators.pattern(/^[A-Z]{4}0[A-Z0-9]{6}$/)]],
    beneficiaryName: ['', [Validators.required, Validators.maxLength(255)]],
    mobile: ['', [Validators.required, Validators.pattern(/^\d{10}$/)]],
    amount: [1000, [Validators.required, Validators.min(1000), Validators.max(10000)]],
    transferMode: ['IMPS' as 'IMPS'|'NEFT'|'RTGS', Validators.required],
    comments: ['', [Validators.required, Validators.maxLength(1000)]],
  });

  submit(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.loading.set(true); this.error.set(''); this.result.set(null);
    this.api.finoDmt({ ...this.form.getRawValue(), idempotencyKey: crypto.randomUUID() })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: value => this.result.set(value), error: response => this.error.set(response?.error?.detail || 'The Fino DMT request could not be completed.') });
  }

  backToHome(): void {
    void this.router.navigate(['/dashboard']);
  }

  closeResult(): void {
    this.result.set(null);
    this.error.set('');
    this.form.reset({ accountNumber: '', ifscCode: '', beneficiaryName: '', mobile: '', amount: 1000, transferMode: 'IMPS', comments: '' });
  }
}
