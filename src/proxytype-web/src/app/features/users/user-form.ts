import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ChannelUserDetails, OrganizationUnit } from '../../core/models';
import { PortalApiService } from '../../core/portal-api.service';

type ChannelType = 'CMF'|'CSF'|'CSP';

@Component({ selector: 'app-user-form', imports: [ReactiveFormsModule, RouterLink], templateUrl: './user-form.html', styleUrl: './user-form.scss' })
export class UserForm {
  private readonly api = inject(PortalApiService);
  private readonly builder = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly userId = this.route.snapshot.paramMap.get('userId');
  private aadhaarDocument: File|null = null;

  readonly editing = signal(!!this.userId);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly organizations = signal<OrganizationUnit[]>([]);
  readonly hasAadhaarDocument = signal(false);
  readonly existingDocumentName = signal('');
  readonly parentOptions = computed(() => {
    const parentType = this.form.controls.unitType.value === 'CMF' ? 'PLATFORM' : this.form.controls.unitType.value === 'CSF' ? 'CMF' : 'CSF';
    return this.organizations().filter(unit => unit.unitType === parentType && unit.status === 'ACTIVE');
  });

  readonly form = this.builder.nonNullable.group({
    unitType: ['CSP' as ChannelType, Validators.required], parentOrganizationUnitId: ['', Validators.required],
    userCode: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(30), Validators.pattern(/^[A-Za-z0-9._-]+$/)]],
    password: [''], prefix: ['Mr', Validators.required], firstName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(30)]],
    middleName: [''], lastName: ['', Validators.required], email: ['', [Validators.required, Validators.email]],
    mobile: ['', [Validators.required, Validators.pattern(/^\d{10}$/)]], fatherName: [''], motherName: [''], dateOfBirth: [''],
    gender: ['M'], maritalStatus: ['S'], panNumber: ['', Validators.pattern(/^$|^[A-Za-z]{5}\d{4}[A-Za-z]$/)], tinNumber: [''],
    marginType: [''], channelType: [''], skuType: [''], skuRemarks: [''], aadhaarNumber: ['', Validators.pattern(/^$|^\d{12}$/)],
    latitude: [''], longitude: [''], asmName: [''],
    companyName: ['', Validators.required], companyAddress1: ['', Validators.required], companyAddress2: [''], companyAddress3: [''],
    companyCity: ['', Validators.required], companyRegion: ['', Validators.required], companyState: ['', Validators.required],
    companyDistrict: ['', Validators.required], companyPincode: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]],
    companyPhone: ['', Validators.pattern(/^$|^\d{6,11}$/)], sameAddress: [false],
    residentialAddress1: ['', Validators.required], residentialAddress2: [''], residentialAddress3: [''],
    residentialCity: ['', Validators.required], residentialRegion: ['', Validators.required], residentialState: ['', Validators.required],
    residentialDistrict: ['', Validators.required], residentialPincode: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]],
    residentialPhone: ['', Validators.pattern(/^$|^\d{6,11}$/)], aadhaarDocumentName: [''], isActive: [true],
  });

  constructor() {
    this.form.controls.unitType.valueChanges.subscribe(() => this.configureTypeValidation());
    this.api.organizationTree().subscribe({
      next: units => { this.organizations.set(units); if (!this.editing()) this.loading.set(false); },
      error: () => { this.error.set('Organization hierarchy could not be loaded.'); this.loading.set(false); },
    });
    if (this.userId) this.loadForEdit(this.userId);
    else {
      const requested = (this.route.snapshot.queryParamMap.get('type') || 'CSP').toUpperCase();
      this.form.controls.unitType.setValue((['CMF','CSF','CSP'].includes(requested) ? requested : 'CSP') as ChannelType);
      this.configureTypeValidation();
    }
  }

  required(controlName: keyof typeof this.form.controls): boolean {
    const control = this.form.controls[controlName];
    return control.touched && control.invalid;
  }
  selectDocument(event: Event): void {
    this.aadhaarDocument = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.form.controls.aadhaarDocumentName.setValue(this.aadhaarDocument?.name ?? '');
    this.form.controls.aadhaarDocumentName.markAsTouched();
  }
  copyCompanyAddress(): void {
    if (!this.form.controls.sameAddress.value) return;
    const value = this.form.getRawValue();
    this.form.patchValue({ residentialAddress1: value.companyAddress1, residentialAddress2: value.companyAddress2,
      residentialAddress3: value.companyAddress3, residentialCity: value.companyCity, residentialRegion: value.companyRegion,
      residentialState: value.companyState, residentialDistrict: value.companyDistrict,
      residentialPincode: value.companyPincode, residentialPhone: value.companyPhone });
  }
  submit(): void {
    this.configureTypeValidation();
    if (this.form.invalid) { this.form.markAllAsTouched(); this.error.set('Complete all required fields and correct the highlighted values.'); return; }
    const data = new FormData();
    const values = this.form.getRawValue();
    for (const [key, value] of Object.entries(values)) {
      if (key === 'sameAddress' || key === 'aadhaarDocumentName' || value === '') continue;
      data.append(key, String(value));
    }
    if (this.aadhaarDocument) data.append('aadhaarDocument', this.aadhaarDocument, this.aadhaarDocument.name);
    this.saving.set(true); this.error.set('');
    const request = this.userId ? this.api.updateChannelUser(this.userId, data) : this.api.createChannelUser(data);
    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => void this.router.navigate(['/users'], { queryParams: { type: values.unitType } }),
      error: response => this.error.set(this.errorMessage(response)),
    });
  }
  downloadDocument(): void {
    if (!this.userId) return;
    this.api.aadhaarDocument(this.userId).subscribe({ next: blob => {
      const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url;
      link.download = this.existingDocumentName() || 'aadhaar-document'; link.click(); URL.revokeObjectURL(url);
    }, error: () => this.error.set('The Aadhaar document could not be downloaded.') });
  }

  private loadForEdit(userId: string): void {
    this.api.channelUser(userId).pipe(finalize(() => this.loading.set(false))).subscribe({ next: details => {
      this.patchDetails(details); this.hasAadhaarDocument.set(details.hasAadhaarDocument);
      this.existingDocumentName.set(details.aadhaarDocumentFileName || ''); this.configureTypeValidation();
    }, error: response => this.error.set(response?.error?.detail || 'User details could not be loaded.') });
  }
  private patchDetails(details: ChannelUserDetails): void {
    const profile = details.profile;
    const name = this.splitDisplayName(details.user.displayName);
    this.form.patchValue({ ...profile,
      prefix: profile.prefix || name.prefix || this.form.controls.prefix.value,
      firstName: profile.firstName || name.firstName,
      middleName: profile.middleName || name.middleName,
      lastName: profile.lastName || name.lastName,
      companyName: profile.companyName || details.organizationName,
      unitType: details.user.memberships[0]?.unitType as ChannelType,
      parentOrganizationUnitId: details.parentOrganizationUnitId, userCode: details.organizationCode,
      email: details.user.email, mobile: details.user.mobile || '', isActive: details.user.isActive,
      latitude: profile.latitude == null ? '' : String(profile.latitude), longitude: profile.longitude == null ? '' : String(profile.longitude) });
  }
  private splitDisplayName(displayName: string): { prefix: string; firstName: string; middleName: string; lastName: string } {
    const parts = displayName.trim().split(/\s+/).filter(Boolean);
    const prefixes = new Set(['MR', 'MRS', 'MS', 'M/S']);
    const hasPrefix = parts.length > 0 && prefixes.has(parts[0].replace(/\.$/, '').toUpperCase());
    const prefix = hasPrefix ? parts.shift()!.replace(/\.$/, '') : '';
    return { prefix, firstName: parts[0] || '', middleName: parts.length > 2 ? parts.slice(1, -1).join(' ') : '',
      lastName: parts.length > 1 ? parts[parts.length - 1] : '' };
  }
  private configureTypeValidation(): void {
    const type = this.form.controls.unitType.value;
    const requiredForCreate = this.editing() ? [] : [Validators.required, Validators.minLength(14)];
    this.form.controls.password.setValidators(requiredForCreate);
    this.setRequired('panNumber', type === 'CMF' || type === 'CSF', Validators.pattern(/^$|^[A-Za-z]{5}\d{4}[A-Za-z]$/));
    this.setRequired('channelType', type === 'CMF' || type === 'CSF');
    this.setRequired('marginType', type === 'CMF'); this.setRequired('skuType', type === 'CSP'); this.setRequired('asmName', type === 'CSP');
    this.setRequired('aadhaarNumber', type === 'CSP', Validators.pattern(/^$|^\d{12}$/));
    this.setRequired('latitude', type === 'CSP'); this.setRequired('longitude', type === 'CSP');
    this.setRequired('aadhaarDocumentName', type === 'CSP' && !this.hasAadhaarDocument());
    this.form.controls.password.updateValueAndValidity({emitEvent:false});
  }
  private setRequired(name: 'panNumber'|'channelType'|'marginType'|'skuType'|'asmName'|'aadhaarNumber'|'latitude'|'longitude'|'aadhaarDocumentName', required: boolean, extra?: ReturnType<typeof Validators.pattern>): void {
    this.form.controls[name].setValidators([...(required ? [Validators.required] : []), ...(extra ? [extra] : [])]);
    this.form.controls[name].updateValueAndValidity({emitEvent:false});
  }
  private errorMessage(response: any): string {
    const errors = response?.error?.errors;
    if (errors) return Object.values(errors).flat().join(' ');
    return response?.error?.detail || response?.error?.title || 'The user could not be saved.';
  }
}
