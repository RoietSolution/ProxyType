import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { UserDocumentItem, UserDocumentOwner } from '../../core/models';
import { PortalApiService } from '../../core/portal-api.service';

@Component({ selector: 'app-user-documents', imports: [DatePipe, DecimalPipe, FormsModule, RouterLink], templateUrl: './user-documents.html', styleUrl: './user-documents.scss' })
export class UserDocuments {
  private readonly api = inject(PortalApiService);
  private selectedFile: File|null = null;
  readonly unitType = signal('CSP'); readonly search = signal(''); readonly owners = signal<UserDocumentOwner[]>([]);
  readonly selectedUserId = signal(''); readonly documents = signal<UserDocumentItem[]>([]); readonly documentType = signal('APPLICATION_FORM');
  readonly fileName = signal(''); readonly loadingUsers = signal(false); readonly loadingDocuments = signal(false);
  readonly uploading = signal(false); readonly error = signal(''); readonly message = signal('');

  constructor() { this.loadOwners(); }
  loadOwners(): void {
    this.loadingUsers.set(true); this.error.set(''); this.selectedUserId.set(''); this.documents.set([]);
    this.api.documentOwners(this.unitType(), this.search()).pipe(finalize(() => this.loadingUsers.set(false))).subscribe({
      next: owners => this.owners.set(owners), error: response => this.error.set(response?.error?.detail || 'Users could not be loaded.'),
    });
  }
  selectUser(value: string): void { this.selectedUserId.set(value); this.documents.set([]); if (value) this.loadDocuments(); }
  loadDocuments(): void {
    const userId = this.selectedUserId(); if (!userId) return;
    this.loadingDocuments.set(true); this.error.set('');
    this.api.userDocuments(userId).pipe(finalize(() => this.loadingDocuments.set(false))).subscribe({
      next: documents => this.documents.set(documents), error: response => this.error.set(response?.error?.detail || 'Documents could not be loaded.'),
    });
  }
  selectFile(event: Event): void { this.selectedFile = (event.target as HTMLInputElement).files?.[0] ?? null; this.fileName.set(this.selectedFile?.name ?? ''); }
  upload(): void {
    const userId = this.selectedUserId(); if (!userId || !this.selectedFile) { this.error.set('Select a user and document file.'); return; }
    const form = new FormData(); form.append('documentType', this.documentType()); form.append('document', this.selectedFile, this.selectedFile.name);
    this.uploading.set(true); this.error.set(''); this.message.set('');
    this.api.uploadUserDocument(userId, form).pipe(finalize(() => this.uploading.set(false))).subscribe({ next: () => {
      this.message.set('Document uploaded successfully.'); this.selectedFile = null; this.fileName.set(''); this.loadDocuments();
    }, error: response => this.error.set(this.errorMessage(response)) });
  }
  download(item: UserDocumentItem): void {
    const userId = this.selectedUserId(); if (!userId) return;
    this.api.downloadUserDocument(userId, item.userDocumentId).subscribe({ next: blob => {
      const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url; link.download = item.fileName; link.click(); URL.revokeObjectURL(url);
    }, error: () => this.error.set('Document download failed.') });
  }
  typeLabel(type: string): string { return ({APPLICATION_FORM:'Application Form',ADDRESS_PROOF:'Address Proof',ID_PROOF:'ID Proof',PAN_CARD:'PAN Card'} as Record<string,string>)[type] || type; }
  private errorMessage(response: any): string { const errors = response?.error?.errors; if (errors) return Object.values(errors).flat().join(' '); return response?.error?.detail || response?.error?.title || 'Document upload failed.'; }
}
