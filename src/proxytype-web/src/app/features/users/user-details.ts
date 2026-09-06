import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, finalize, forkJoin, of } from 'rxjs';
import { ChannelUserDetails, ChannelUserProfile, EffectiveService, ServiceDefinition, UserDocumentItem } from '../../core/models';
import { PortalApiService } from '../../core/portal-api.service';

type DetailTab = 'overview'|'address'|'documents'|'services'|'security';

@Component({ selector: 'app-user-details', imports: [ReactiveFormsModule, RouterLink], templateUrl: './user-details.html', styleUrl: './user-details.scss' })
export class UserDetails {
  private readonly api = inject(PortalApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly builder = inject(FormBuilder);

  readonly details = signal<ChannelUserDetails|null>(null);
  readonly documents = signal<UserDocumentItem[]>([]);
  readonly services = signal<EffectiveService[]>([]);
  readonly serviceDefinitions = signal<ServiceDefinition[]>([]);
  readonly activeTab = signal<DetailTab>('overview');
  readonly loading = signal(true);
  readonly error = signal('');
  readonly message = signal('');
  readonly password = this.builder.nonNullable.control('', [Validators.required, Validators.minLength(14)]);

  readonly profile = computed<ChannelUserProfile>(() => this.details()?.profile ?? {});
  readonly unitType = computed(() => this.details()?.user.memberships[0]?.unitType ?? 'CSP');
  readonly fullName = computed(() => {
    const profile = this.profile();
    return [profile.prefix, profile.firstName, profile.middleName, profile.lastName].filter(Boolean).join(' ') || this.details()?.user.displayName || '';
  });
  readonly allowedServices = computed(() => this.services().filter(service => service.isAllowed).length);
  readonly restrictedServices = computed(() => this.services().filter(service => !service.isAllowed).length);
  readonly kycChecks = computed(() => {
    const profile = this.profile();
    const documentTypes = new Set(this.documents().map(document => document.documentType));
    const common = [
      { label: 'Identity proof', complete: documentTypes.has('ID_PROOF') },
      { label: 'Address proof', complete: documentTypes.has('ADDRESS_PROOF') },
    ];
    return this.unitType() === 'CSP'
      ? [{ label: 'Aadhaar number', complete: !!profile.aadhaarNumber }, { label: 'Aadhaar document', complete: !!this.details()?.hasAadhaarDocument }, ...common]
      : [{ label: 'PAN number', complete: !!profile.panNumber }, { label: 'PAN card', complete: documentTypes.has('PAN_CARD') }, ...common];
  });
  readonly kycReady = computed(() => this.kycChecks().every(check => check.complete));
  readonly completedKycChecks = computed(() => this.kycChecks().filter(check => check.complete).length);
  readonly kycRemarks = computed(() => {
    const missing = this.kycChecks().filter(check => !check.complete).map(check => check.label);
    return missing.length ? `Pending items: ${missing.join(', ')}.` : 'All required KYC information is available for administrative review.';
  });

  constructor() {
    const userId = this.route.snapshot.paramMap.get('userId');
    if (!userId) { this.error.set('A user was not selected.'); this.loading.set(false); return; }
    this.api.channelUser(userId).subscribe({
      next: details => {
        this.details.set(details);
        forkJoin({
          documents: this.api.userDocuments(userId).pipe(catchError(() => of([] as UserDocumentItem[]))),
          services: this.api.effectiveServices(details.organizationUnitId).pipe(catchError(() => of([] as EffectiveService[]))),
          definitions: this.api.services().pipe(catchError(() => of([] as ServiceDefinition[]))),
        }).pipe(finalize(() => this.loading.set(false))).subscribe(result => {
          this.documents.set(result.documents); this.services.set(result.services); this.serviceDefinitions.set(result.definitions);
        });
      },
      error: response => { this.error.set(response?.error?.detail || 'Unable to load the channel user profile.'); this.loading.set(false); },
    });
  }

  selectTab(tab: DetailTab): void { this.activeTab.set(tab); }
  resetPassword(): void {
    const details = this.details();
    if (!details || this.password.invalid) { this.password.markAsTouched(); return; }
    this.message.set(''); this.error.set('');
    this.api.resetUserPassword(details.user.userId, this.password.value).subscribe({
      next: () => { this.message.set('Password reset saved. The user must change it at next login.'); this.password.reset(); },
      error: response => this.error.set(response?.error?.detail || 'Unable to reset password.'),
    });
  }
  downloadAadhaar(): void {
    const details = this.details(); if (!details) return;
    this.api.aadhaarDocument(details.user.userId).subscribe({
      next: blob => this.saveBlob(blob, details.aadhaarDocumentFileName || 'aadhaar-document'),
      error: () => this.error.set('The Aadhaar document could not be downloaded.'),
    });
  }
  downloadDocument(document: UserDocumentItem): void {
    const details = this.details(); if (!details) return;
    this.api.downloadUserDocument(details.user.userId, document.userDocumentId).subscribe({
      next: blob => this.saveBlob(blob, document.fileName), error: () => this.error.set(`${document.fileName} could not be downloaded.`),
    });
  }
  back(): void { void this.router.navigate(['/users'], { queryParams: { type: this.unitType() } }); }
  documentTypeLabel(type: UserDocumentItem['documentType']): string {
    return ({ APPLICATION_FORM: 'Application form', ADDRESS_PROOF: 'Address proof', ID_PROOF: 'Identity proof', PAN_CARD: 'PAN card' })[type];
  }
  formatDate(value: string): string { return new Date(value).toLocaleString(); }
  formatSize(bytes: number): string { return bytes < 1024 * 1024 ? `${Math.ceil(bytes / 1024)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`; }
  requiresKyc(serviceId: string): boolean { return this.serviceDefinitions().find(service => service.serviceId === serviceId)?.requiresKyc ?? false; }
  maskedAadhaar(): string { const value = this.profile().aadhaarNumber || ''; return value ? `XXXX XXXX ${value.slice(-4)}` : 'Not provided'; }
  maskedPan(): string { const value = this.profile().panNumber || ''; return value ? `${value.slice(0, 3)}****${value.slice(-3)}` : 'Not provided'; }
  address(parts: Array<string|undefined>): string { return parts.filter(value => !!value?.trim()).join(', ') || 'Not provided'; }

  private saveBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob); const link = document.createElement('a');
    link.href = url; link.download = fileName; link.click(); URL.revokeObjectURL(url);
  }
}
