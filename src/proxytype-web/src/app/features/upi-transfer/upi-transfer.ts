import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { UpiFundingResponse } from '../../core/models';
import { PortalApiService } from '../../core/portal-api.service';

@Component({ selector: 'app-upi-transfer', imports: [ReactiveFormsModule], templateUrl: './upi-transfer.html', styleUrl: './upi-transfer.scss' })
export class UpiTransfer {
  private readonly builder = inject(FormBuilder); private readonly api = inject(PortalApiService); private readonly router = inject(Router);
  readonly loading = signal(false); readonly checking = signal(false); readonly error = signal(''); readonly result = signal<UpiFundingResponse|null>(null);
  readonly form = this.builder.nonNullable.group({ amount: [100, [Validators.required, Validators.min(1), Validators.max(100000)]] });

  submit(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.loading.set(true); this.error.set(''); this.result.set(null);
    this.api.upiFundingOrder({ amount: this.form.getRawValue().amount, idempotencyKey: crypto.randomUUID() }).pipe(finalize(() => this.loading.set(false))).subscribe({ next: value => this.result.set(value), error: response => this.error.set(response?.error?.detail || 'The UPI funding request could not be created.') });
  }

  checkStatus(): void {
    const transaction = this.result(); if (!transaction) return;
    this.checking.set(true); this.error.set('');
    this.api.upiFundingStatus(transaction.transactionId).pipe(finalize(() => this.checking.set(false))).subscribe({ next: value => this.result.set(value), error: response => this.error.set(response?.error?.detail || 'The payment status could not be checked.') });
  }

  backToHome(): void { void this.router.navigate(['/dashboard']); }
  closeResult(): void { this.result.set(null); this.error.set(''); this.form.reset({ amount: 100 }); }
}
