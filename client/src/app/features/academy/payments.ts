import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, FormState, Notify, PagedList, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { PAYMENT_METHODS, Paged, Payment, PaymentSummary, Permission } from '../../core/models';
import { UI } from '../../shared/ui';
import { PaymentDialogComponent, PaymentSaved, savedMessage } from './payment-dialog';

@Component({
  selector: 'app-payments',
  imports: [RouterLink, UI, PaymentDialogComponent],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Payments</h1>
        <p>Every fee payment and refund, with its receipt.</p>
      </div>
      <div class="page-actions">
        @if (can(permission.LedgerCorrect)) {
          <button type="button" class="secondary" (click)="dialog.set('refund')">Refund</button>
        }
        @if (can(permission.PaymentsWrite)) {
          <button type="button" class="primary" (click)="dialog.set('payment')">+ Record payment</button>
        }
      </div>
    </div>

    @if (list.error()) {
      <div class="alert" role="alert">{{ list.error() }}</div>
    }

    @if (summary(); as s) {
      <div class="stats four">
        <article class="stat green"><div class="stat-top">NET COLLECTED</div><div class="stat-value">{{ s.net | money }}</div><small>Received minus refunds</small></article>
        <article class="stat purple"><div class="stat-top">THIS MONTH</div><div class="stat-value">{{ s.thisMonth | money }}</div></article>
        <article class="stat white"><div class="stat-top">PAYMENTS</div><div class="stat-value">{{ s.count }}</div></article>
        <article class="stat rose"><div class="stat-top">REFUNDED</div><div class="stat-value">{{ s.refunded | money }}</div></article>
      </div>
    }

    <div class="filter-bar">
      <input placeholder="⌕  Search by student or note…" [ngModel]="search()" (ngModelChange)="onSearch($event)" />
      <select [ngModel]="method()" (ngModelChange)="method.set($event); list.reload()" aria-label="Method">
        <option>All</option>
        @for (m of methods; track m) {
          <option>{{ m }}</option>
        }
      </select>
      <input type="date" [ngModel]="from()" (ngModelChange)="from.set($event); list.reload()" aria-label="From date" />
      <input type="date" [ngModel]="to()" (ngModelChange)="to.set($event); list.reload()" aria-label="To date" />
    </div>

    <div class="surface table-scroll">
      <table>
        <thead>
          <tr><th>RECEIPT</th><th>STUDENT</th><th>DATE</th><th>METHOD</th><th>AMOUNT</th><th>STATUS</th><th></th></tr>
        </thead>
        <tbody>
          @for (p of list.items(); track p.id) {
            <tr [class.dimmed]="p.status === 'Voided'">
              <td><strong>{{ p.receipt }}</strong><small>{{ p.kind }}</small></td>
              <td><strong>{{ p.studentName }}</strong><small>{{ p.status === 'Voided' ? 'Voided: ' + p.voidReason : p.notes }}</small></td>
              <td>{{ p.paidOn | day }}</td>
              <td>{{ p.method }}</td>
              <td [class.positive]="p.kind === 'Payment'" [class.negative]="p.kind === 'Refund'">{{ p.kind === 'Refund' ? '−' : '' }}{{ p.amount | money: true }}</td>
              <td><app-status [value]="p.status" /></td>
              <td class="row-actions">
                <a class="table-action" [routerLink]="['/app/payments', p.id, 'receipt']">Receipt</a>
                @if (p.status === 'Posted' && can(permission.LedgerCorrect)) {
                  <button type="button" class="table-action danger-text" (click)="voidTarget.set(p)">Void</button>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="7" class="empty">{{ list.loading() ? 'Loading…' : 'No payments found.' }}</td></tr>
          }
        </tbody>
      </table>
      <app-pager [page]="list.page()" [total]="list.total()" (pageChange)="list.load($event)" />
    </div>

    @if (dialog(); as mode) {
      <app-payment-dialog [refund]="mode === 'refund'" (closed)="dialog.set(null)" (saved)="saved($event, mode === 'refund')" />
    }

    @if (voidTarget(); as target) {
      <app-reason-dialog
        [title]="'Void ' + target.receipt"
        description="The entry stays on record, marked as voided, and no longer counts towards the student's balance."
        confirmLabel="Void entry"
        [busy]="voiding.busy()"
        [error]="voiding.error()"
        (cancelled)="voidTarget.set(null)"
        (confirmed)="confirmVoid(target, $event)"
      />
    }
  `,
})
export class PaymentsComponent {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(Notify);

  protected readonly permission = Permission;
  protected readonly methods = PAYMENT_METHODS;
  protected readonly search = signal('');
  protected readonly method = signal('All');
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly summary = signal<PaymentSummary | null>(null);
  protected readonly dialog = signal<'payment' | 'refund' | null>(null);
  protected readonly voidTarget = signal<Payment | null>(null);
  protected readonly voiding = new FormState();
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly list = new PagedList<Payment>((page, pageSize) =>
    this.api.get<Paged<Payment>>('payments', { page, pageSize, search: this.search(), method: this.method(), from: this.from(), to: this.to() }),
  );

  constructor() {
    this.reload();
  }

  protected can(permission: string): boolean {
    return this.auth.can(permission);
  }

  private reload(): void {
    this.list.load();
    this.api.get<PaymentSummary>('payments/summary').subscribe({ next: s => this.summary.set(s), error: e => this.list.error.set(errorMessage(e)) });
  }

  protected onSearch(text: string): void {
    this.search.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.list.reload(), 300);
  }

  protected saved(result: PaymentSaved, refund: boolean): void {
    this.dialog.set(null);
    this.notify.success(savedMessage(result, refund));
    this.reload();
  }

  protected confirmVoid(payment: Payment, reason: string): void {
    this.voiding.submit(this.api.post(`payments/${payment.id}/void`, { reason }), () => {
      this.voidTarget.set(null);
      this.notify.success(`${payment.receipt} voided`);
      this.reload();
    });
  }
}
