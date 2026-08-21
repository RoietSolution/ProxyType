import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { FundRequestResponse } from '../../core/models';
import { PortalApiService } from '../../core/portal-api.service';

@Component({ selector: 'app-fund-request', imports: [DatePipe, ReactiveFormsModule], templateUrl: './fund-request.html', styleUrl: './fund-request.scss' })
export class FundRequest implements OnInit {
  private readonly builder = inject(FormBuilder);
  private readonly api = inject(PortalApiService);
  private readonly router = inject(Router);
  readonly form = this.builder.nonNullable.group({ amount: [1, [Validators.required, Validators.min(1), Validators.max(1000000)]], externalReference: ['', [Validators.required, Validators.maxLength(150)]] });
  readonly proof = signal<File | null>(null);
  readonly records = signal<FundRequestResponse[]>([]);
  readonly result = signal<FundRequestResponse | null>(null);
  readonly loading = signal(false);
  readonly loadingRecords = signal(true);
  readonly reviewing = signal('');
  readonly error = signal('');

  ngOnInit(): void { this.load(); }

  selectProof(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.proof.set(file);
    if (file) this.error.set('');
  }

  submit(): void {
    const file = this.proof();
    if (this.form.invalid || !file) { this.form.markAllAsTouched(); if (!file) this.error.set('Upload the NEFT payment screenshot/proof.'); return; }
    const data = new FormData();
    data.append('amount', String(this.form.controls.amount.value));
    data.append('externalReference', this.form.controls.externalReference.value.trim());
    data.append('idempotencyKey', crypto.randomUUID());
    data.append('proof', file, file.name);
    this.loading.set(true); this.error.set(''); this.result.set(null);
    this.api.createFundRequest(data).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: value => { this.result.set(value); this.load(); },
      error: response => this.error.set(response?.error?.detail || response?.error?.title || 'Fund Request could not be created.')
    });
  }

  review(record: FundRequestResponse, decision: 'APPROVED'|'REJECTED'): void {
    this.reviewing.set(record.fundRequestId); this.error.set('');
    this.api.reviewFundRequest(record.fundRequestId, decision).pipe(finalize(() => this.reviewing.set(''))).subscribe({
      next: value => { this.records.update(items => items.map(item => item.fundRequestId === value.fundRequestId ? value : item)); if (this.result()?.fundRequestId === value.fundRequestId) this.result.set(value); },
      error: response => this.error.set(response?.error?.detail || response?.error?.title || 'Fund Request review failed.')
    });
  }

  reset(): void { this.result.set(null); this.error.set(''); this.proof.set(null); this.form.reset({ amount: 1, externalReference: '' }); }
  close(): void { void this.router.navigate(['/dashboard']); }

  private load(): void {
    this.loadingRecords.set(true);
    this.api.fundRequests().pipe(finalize(() => this.loadingRecords.set(false))).subscribe({ next: value => this.records.set(value), error: response => this.error.set(response?.error?.detail || response?.error?.title || 'Fund Request history could not be loaded.') });
  }
}
