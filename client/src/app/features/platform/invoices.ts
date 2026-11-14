import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, PagedList } from '../../core/api';
import { Invoice, Paged } from '../../core/models';
import { UI } from '../../shared/ui';

/** Every subscription invoice across all academies. Paying and voiding happen on the academy's own page. */
@Component({
  selector: 'app-invoices',
  imports: [RouterLink, UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">PLATFORM</div>
        <h1>Invoices</h1>
        <p>Subscription invoices across all academies.</p>
      </div>
    </div>

    @if (list.error()) {
      <div class="alert" role="alert">{{ list.error() }}</div>
    }

    <div class="filter-bar">
      <select [ngModel]="status()" (ngModelChange)="status.set($event); list.reload()" aria-label="Status">
        <option>All</option><option>Open</option><option>Paid</option><option>Void</option>
      </select>
    </div>

    <div class="surface table-scroll">
      <table>
        <thead><tr><th>INVOICE</th><th>ACADEMY</th><th>PERIOD</th><th>DUE</th><th>AMOUNT</th><th>STATUS</th><th></th></tr></thead>
        <tbody>
          @for (invoice of list.items(); track invoice.id) {
            <tr>
              <td><strong>{{ invoice.invoiceNumber }}</strong><small>{{ invoice.planName }}</small></td>
              <td>{{ invoice.tenantName }}</td>
              <td>{{ invoice.periodStart | day }} – {{ invoice.periodEnd | day }}</td>
              <td>{{ invoice.dueOn | day }}</td>
              <td>{{ invoice.amount | money: true }}</td>
              <td>
                <app-status [value]="invoice.status" />
                @if (invoice.status === 'Paid') {
                  <small>{{ invoice.paymentMethod }} · {{ invoice.paidAtUtc | day }}</small>
                }
              </td>
              <td class="row-actions"><a class="table-action" [routerLink]="['/platform/academies', invoice.tenantId]">Open academy →</a></td>
            </tr>
          } @empty {
            <tr><td colspan="7" class="empty">{{ list.loading() ? 'Loading…' : 'No invoices found.' }}</td></tr>
          }
        </tbody>
      </table>
      <app-pager [page]="list.page()" [total]="list.total()" (pageChange)="list.load($event)" />
    </div>
  `,
})
export class InvoicesComponent {
  private readonly api = inject(Api);
  protected readonly status = signal('All');
  protected readonly list = new PagedList<Invoice>((page, pageSize) =>
    this.api.get<Paged<Invoice>>('platform/invoices', { page, pageSize, status: this.status() }),
  );

  constructor() {
    this.list.load();
  }
}
