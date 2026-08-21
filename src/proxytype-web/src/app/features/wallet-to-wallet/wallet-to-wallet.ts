import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { PortalApiService } from '../../core/portal-api.service';
import { WalletTransferReceiver, WalletTransferResponse } from '../../core/models';

@Component({ selector: 'app-wallet-to-wallet', imports: [ReactiveFormsModule], templateUrl: './wallet-to-wallet.html', styleUrl: './wallet-to-wallet.scss' })
export class WalletToWallet {
  private readonly builder = inject(FormBuilder); private readonly api = inject(PortalApiService); private readonly router = inject(Router);
  readonly loading = signal(false); readonly lookupLoading = signal(false); readonly error = signal(''); readonly receiver = signal<WalletTransferReceiver|null>(null); readonly result = signal<WalletTransferResponse|null>(null);
  readonly form = this.builder.nonNullable.group({ receiverMobile: ['', [Validators.required, Validators.pattern(/^\d{10}$/)]], amount: [10, [Validators.required, Validators.min(10), Validators.max(1000000)]] });
  lookup(): void { const mobile = this.form.controls.receiverMobile.value; this.receiver.set(null); this.error.set(''); if (!/^\d{10}$/.test(mobile)) { this.form.controls.receiverMobile.markAsTouched(); return; } this.lookupLoading.set(true); this.api.walletReceiver(mobile).pipe(finalize(() => this.lookupLoading.set(false))).subscribe({ next: value => this.receiver.set(value), error: response => this.error.set(response?.error?.title || 'Receiver is not available in your permitted hierarchy.') }); }
  submit(): void { if (this.form.invalid || !this.receiver()) { this.form.markAllAsTouched(); return; } this.loading.set(true); this.error.set(''); this.result.set(null); this.api.walletTransfer({ ...this.form.getRawValue(), idempotencyKey: crypto.randomUUID() }).pipe(finalize(() => this.loading.set(false))).subscribe({ next: value => this.result.set(value), error: response => this.error.set(response?.error?.detail || response?.error?.title || 'Wallet transfer could not be completed.') }); }
  close(): void { void this.router.navigate(['/dashboard']); }
  reset(): void { this.result.set(null); this.receiver.set(null); this.error.set(''); this.form.reset({ receiverMobile: '', amount: 10 }); }
}
