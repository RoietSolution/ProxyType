import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { ChargeSlab, PricingMetadata } from '../../core/models';
import { PortalApiService } from '../../core/portal-api.service';

@Component({ selector: 'app-charge-slabs', imports: [ReactiveFormsModule], templateUrl: './charge-slabs.html', styleUrl: './charge-slabs.scss' })
export class ChargeSlabs {
  private readonly api = inject(PortalApiService);
  private readonly builder = inject(FormBuilder);
  private readonly router = inject(Router);
  readonly metadata = signal<PricingMetadata>({ services: [], roles: [] });
  readonly slabs = signal<ChargeSlab[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly success = signal('');
  readonly editorPanel = viewChild<ElementRef<HTMLElement>>('editorPanel');
  readonly selection = this.builder.nonNullable.group({ serviceId: ['', Validators.required], roleId: ['', Validators.required] });
  readonly form = this.builder.nonNullable.group({ pricingRuleId: [''], amountFrom: [0, [Validators.required, Validators.min(0)]], amountTo: [0, [Validators.required, Validators.min(0)]], calculationType: ['FIXED' as 'FIXED'|'PERCENTAGE', Validators.required], rate: [0, [Validators.required, Validators.min(0)]], tdsRate: [0, [Validators.required, Validators.min(0), Validators.max(100)]], gstRate: [0, [Validators.required, Validators.min(0), Validators.max(100)]] });

  constructor() {
    this.api.chargeSlabMetadata().subscribe({ next: data => this.metadata.set(data), error: response => this.fail(response, 'Charge slab metadata could not be loaded.') });
  }
  load(): void {
    if (this.selection.invalid) { this.selection.markAllAsTouched(); this.error.set('Select both a service and a role.'); this.success.set(''); return; }
    const {serviceId, roleId} = this.selection.getRawValue(); this.loading.set(true); this.error.set(''); this.success.set('');
    this.api.chargeSlabs(serviceId, roleId).pipe(finalize(() => this.loading.set(false))).subscribe({ next: rows => this.slabs.set(rows), error: response => this.fail(response, 'Charge slabs could not be loaded.') });
  }
  edit(row: ChargeSlab): void {
    this.selection.setValue({ serviceId: row.serviceId, roleId: row.roleId });
    this.form.setValue({ pricingRuleId: row.pricingRuleId, amountFrom: row.amountFrom, amountTo: row.amountTo,
      calculationType: row.calculationType, rate: row.rate, tdsRate: row.tdsRate, gstRate: row.gstRate });
    this.error.set(''); this.success.set('');
    queueMicrotask(() => this.editorPanel()?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }
  reset(): void { this.form.reset({ pricingRuleId: '', amountFrom: 0, amountTo: 0, calculationType: 'FIXED', rate: 0, tdsRate: 0, gstRate: 0 }); this.error.set(''); }
  save(): void {
    this.success.set('');
    if (this.selection.invalid || this.form.invalid) { this.selection.markAllAsTouched(); this.form.markAllAsTouched(); this.error.set('Complete all fields with valid non-negative values before saving.'); return; }
    const selected = this.selection.getRawValue(); const value = this.form.getRawValue();
    if (value.amountTo < value.amountFrom) { this.error.set('Amount To must be greater than or equal to Amount From.'); return; }
    this.saving.set(true); this.error.set('');
    const wasEditing = !!value.pricingRuleId;
    this.api.saveChargeSlab({ ...value, pricingRuleId: value.pricingRuleId || null, ...selected }).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: saved => {
        this.slabs.update(rows => [...rows.filter(row => row.pricingRuleId !== saved.pricingRuleId), saved].sort((a, b) => a.amountFrom - b.amountFrom));
        this.reset(); this.success.set(wasEditing ? 'Charge slab updated successfully.' : 'Charge slab added successfully.');
      },
      error: response => this.fail(response, 'Charge slab could not be saved.'),
    });
  }
  remove(row: ChargeSlab): void { if (!confirm(`Remove the ${row.amountFrom}–${row.amountTo} charge slab?`)) return; this.api.deleteChargeSlab(row.pricingRuleId).subscribe({ next: () => this.load(), error: response => this.fail(response, 'Charge slab could not be removed.') }); }
  close(): void { void this.router.navigate(['/dashboard']); }
  private fail(response: any, fallback: string): void { this.success.set(''); const validation = response?.error?.errors ? Object.values(response.error.errors).flat().join(' ') : ''; this.error.set(response?.error?.detail || validation || response?.error?.title || fallback); }
}
