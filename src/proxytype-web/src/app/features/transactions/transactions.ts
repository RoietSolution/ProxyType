import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { TransactionHistoryItem } from '../../core/models';
import { PortalApiService } from '../../core/portal-api.service';

@Component({ selector: 'app-transactions', imports: [FormsModule, DatePipe, RouterLink], templateUrl: './transactions.html', styleUrl: './transactions.scss' })
export class Transactions implements OnInit {
  private readonly api = inject(PortalApiService);
  readonly rows = signal<TransactionHistoryItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly search = signal('');
  readonly status = signal('');
  readonly page = signal(1);

  ngOnInit(): void { this.load(); }
  load(): void {
    this.loading.set(true); this.error.set('');
    this.api.transactions({ search: this.search(), status: this.status(), page: this.page() })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: rows => this.rows.set(rows), error: () => this.error.set('Transaction history could not be loaded.') });
  }
  apply(): void { this.page.set(1); this.load(); }
  next(): void { if (this.rows().length === 25) { this.page.update(value => value + 1); this.load(); } }
  previous(): void { if (this.page() > 1) { this.page.update(value => value - 1); this.load(); } }
}
