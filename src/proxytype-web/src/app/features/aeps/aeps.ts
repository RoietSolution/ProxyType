import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AepsBank, AepsResponse, AepsTransactionType } from '../../core/models';
import { PortalApiService } from '../../core/portal-api.service';

@Component({ selector: 'app-aeps', imports: [ReactiveFormsModule], templateUrl: './aeps.html', styleUrl: './aeps.scss' })
export class Aeps implements OnInit {
  private readonly builder = inject(FormBuilder); private readonly api = inject(PortalApiService); private readonly router = inject(Router);
  readonly banks = signal<AepsBank[]>([]); readonly loading = signal(false); readonly error = signal(''); readonly result = signal<AepsResponse|null>(null);
  readonly form = this.builder.nonNullable.group({ transactionType: ['CASH_WITHDRAWAL' as AepsTransactionType, Validators.required], aadhaarNumber: ['', [Validators.required, Validators.pattern(/^\d{12}$/)]], mobileNumber: ['', [Validators.required, Validators.pattern(/^\d{10}$/)]], bankIin: ['', Validators.required], deviceName: ['', [Validators.required, Validators.maxLength(100)]], biometricCaptureReference: ['', [Validators.required, Validators.maxLength(100)]], amount: [500 as number|null, [Validators.min(100), Validators.max(10000)]] });
  ngOnInit(): void { this.api.aepsBanks().subscribe({ next: value => { this.banks.set(value); if (value[0]) this.form.patchValue({ bankIin: value[0].iin }); }, error: () => this.error.set('Banks could not be loaded.') }); }
  isCash(): boolean { return this.form.controls.transactionType.value === 'CASH_WITHDRAWAL'; }
  submit(): void { if (!this.isCash()) this.form.controls.amount.setValue(null); if (this.form.invalid || (this.isCash() && !this.form.controls.amount.value)) { this.form.markAllAsTouched(); return; } this.loading.set(true); this.error.set(''); this.result.set(null); this.api.aeps({ ...this.form.getRawValue(), idempotencyKey: crypto.randomUUID() }).pipe(finalize(() => this.loading.set(false))).subscribe({ next: value => this.result.set(value), error: response => this.error.set(response?.error?.detail || response?.error?.title || 'The AEPS request could not be completed.') }); }
  backToHome(): void { void this.router.navigate(['/dashboard']); }
  newTransaction(): void { this.result.set(null); this.error.set(''); this.form.patchValue({ aadhaarNumber: '', mobileNumber: '', deviceName: '', biometricCaptureReference: '', amount: 500 }); }
}
