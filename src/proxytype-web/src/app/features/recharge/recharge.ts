import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { PortalApiService } from '../../core/portal-api.service';
import { RechargeOperator, RechargeResponse } from '../../core/models';

@Component({ selector: 'app-recharge', imports: [ReactiveFormsModule], templateUrl: './recharge.html', styleUrl: './recharge.scss' })
export class Recharge {
  private readonly builder = inject(FormBuilder);
  private readonly api = inject(PortalApiService);
  private readonly router = inject(Router);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly result = signal<RechargeResponse|null>(null);
  readonly operators = signal<RechargeOperator[]>([]);
  readonly form = this.builder.nonNullable.group({
    account: ['', [Validators.required, Validators.maxLength(50)]], amount: [10, [Validators.required, Validators.min(10), Validators.max(100000)]],
    rechargeType: ['MOBILE' as 'MOBILE'|'DTH'|'GOOGLE'|'LAPU', Validators.required], operator: ['', Validators.required],
    pincode: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]], latitude: [0, [Validators.required, Validators.min(-90), Validators.max(90)]], longitude: [0, [Validators.required, Validators.min(-180), Validators.max(180)]]
  });

  constructor() { this.loadOperators('MOBILE'); }
  changeType(): void { const type = this.form.controls.rechargeType.value; this.form.controls.operator.setValue(''); this.loadOperators(type); }
  submit(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.loading.set(true); this.error.set(''); this.result.set(null);
    this.api.recharge({ ...this.form.getRawValue(), idempotencyKey: crypto.randomUUID() }).pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: value => this.result.set(value), error: response => this.error.set(response?.error?.detail || 'The recharge request could not be completed.') });
  }
  backToHome(): void { void this.router.navigate(['/dashboard']); }
  closeResult(): void { this.result.set(null); this.error.set(''); }
  private loadOperators(type: string): void { this.api.rechargeOperators(type).subscribe({ next: value => this.operators.set(value), error: () => this.operators.set([]) }); }
}
