import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { FundRequestInstructions, FundRequestResponse } from '../../core/models';
import { PortalApiService } from '../../core/portal-api.service';

@Component({ selector: 'app-fund-request', imports: [DatePipe, ReactiveFormsModule], templateUrl: './fund-request.html', styleUrls: ['./fund-request.scss', './fund-request-deposit.scss'] })
export class FundRequest implements OnInit {
  private static readonly maximumProofBytes = 5_000_000;
  private static readonly allowedProofTypes = new Set(['image/jpeg', 'image/png', 'image/gif']);
  private readonly builder = inject(FormBuilder);
  private readonly api = inject(PortalApiService);
  private readonly router = inject(Router);
  readonly today = this.localDate(new Date());
  readonly form = this.builder.nonNullable.group({ amount: [1, [Validators.required, Validators.min(1), Validators.max(1000000)]], transactionDate: [this.today, Validators.required], externalReference: ['', [Validators.required, Validators.maxLength(150)]] });
  readonly proof = signal<File | null>(null);
  readonly records = signal<FundRequestResponse[]>([]);
  readonly instructions = signal<FundRequestInstructions | null>(null);
  readonly result = signal<FundRequestResponse | null>(null);
  readonly loading = signal(false);
  readonly loadingRecords = signal(true);
  readonly reviewing = signal('');
  readonly error = signal('');

  ngOnInit(): void {
    this.load();
    this.api.fundRequestInstructions().subscribe({
      next: value => this.instructions.set(value),
      error: response => this.error.set(response?.error?.detail || 'Deposit instructions could not be loaded.')
    });
  }

  selectProof(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    if (file && (!FundRequest.allowedProofTypes.has(file.type) || file.size > FundRequest.maximumProofBytes)) {
      input.value = '';
      this.proof.set(null);
      this.error.set('Proof must be a JPG, PNG, or GIF image up to 5 MB.');
      return;
    }
    this.proof.set(file);
    if (file) this.error.set('');
  }

  submit(): void {
    const file = this.proof();
    if (this.form.invalid || !file) { this.form.markAllAsTouched(); if (!file) this.error.set('Upload the NEFT payment screenshot/proof.'); return; }
    const data = new FormData();
    data.append('amount', String(this.form.controls.amount.value));
    data.append('transactionDate', this.form.controls.transactionDate.value);
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

  reset(): void { this.result.set(null); this.error.set(''); this.proof.set(null); this.form.reset({ amount: 1, transactionDate: this.today, externalReference: '' }); }
  close(): void { void this.router.navigate(['/dashboard']); }

  private load(): void {
    this.loadingRecords.set(true);
    this.api.fundRequests().pipe(finalize(() => this.loadingRecords.set(false))).subscribe({ next: value => this.records.set(value), error: response => this.error.set(response?.error?.detail || response?.error?.title || 'Fund Request history could not be loaded.') });
  }
  private localDate(value: Date): string {
    const offset = value.getTimezoneOffset() * 60_000;
    return new Date(value.getTime() - offset).toISOString().slice(0, 10);
  }
}
