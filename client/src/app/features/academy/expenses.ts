import { Component, inject, signal } from '@angular/core';
import { Api, FormState, Notify, PagedList, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { Expense, ExpenseSummary, PAYMENT_METHODS, Paged, Permission } from '../../core/models';
import { UI, today } from '../../shared/ui';

@Component({
  selector: 'app-expenses',
  imports: [UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Expenses</h1>
        <p>Operating spend, by category.</p>
      </div>
      @if (can(permission.ExpensesWrite)) {
        <button type="button" class="primary" (click)="open()">+ Add expense</button>
      }
    </div>

    @if (list.error()) {
      <div class="alert" role="alert">{{ list.error() }}</div>
    }

    @if (summary(); as s) {
      <div class="stats four">
        <article class="stat pink"><div class="stat-top">TOTAL EXPENSES</div><div class="stat-value">{{ s.total | money }}</div></article>
        <article class="stat yellow"><div class="stat-top">THIS MONTH</div><div class="stat-value">{{ s.thisMonth | money }}</div></article>
        <article class="stat white"><div class="stat-top">ENTRIES</div><div class="stat-value">{{ s.count }}</div></article>
        <article class="stat white"><div class="stat-top">TOP CATEGORY</div><div class="stat-value small-value">{{ s.topCategory ?? '—' }}</div></article>
      </div>
    }

    <div class="filter-bar">
      <input placeholder="⌕  Search expenses…" [ngModel]="search()" (ngModelChange)="onSearch($event)" />
      <select [ngModel]="category()" (ngModelChange)="category.set($event); list.reload()" aria-label="Category">
        <option>All</option>
        @for (c of summary()?.categories ?? []; track c) {
          <option>{{ c }}</option>
        }
      </select>
      <input type="date" [ngModel]="from()" (ngModelChange)="from.set($event); list.reload()" aria-label="From date" />
      <input type="date" [ngModel]="to()" (ngModelChange)="to.set($event); list.reload()" aria-label="To date" />
    </div>

    <div class="surface table-scroll">
      <table>
        <thead><tr><th>TITLE</th><th>CATEGORY</th><th>DATE</th><th>METHOD</th><th>AMOUNT</th><th>STATUS</th><th></th></tr></thead>
        <tbody>
          @for (e of list.items(); track e.id) {
            <tr [class.dimmed]="e.status === 'Voided'">
              <td><strong>{{ e.title }}</strong><small>{{ e.status === 'Voided' ? 'Voided: ' + e.voidReason : e.notes }}</small></td>
              <td>{{ e.category }}</td>
              <td>{{ e.spentOn | day }}</td>
              <td>{{ e.method }}</td>
              <td class="negative">{{ e.amount | money: true }}</td>
              <td><app-status [value]="e.status" /></td>
              <td class="row-actions">
                @if (e.status === 'Posted' && can(permission.LedgerCorrect)) {
                  <button type="button" class="table-action danger-text" (click)="voidTarget.set(e)">Void</button>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="7" class="empty">{{ list.loading() ? 'Loading…' : 'No expenses found.' }}</td></tr>
          }
        </tbody>
      </table>
      <app-pager [page]="list.page()" [total]="list.total()" (pageChange)="list.load($event)" />
    </div>

    @if (form.open()) {
      <app-modal title="Add expense" [busy]="form.busy()" [error]="form.error()" (closed)="form.close()" (saved)="save()">
        <label for="title">Title *</label>
        <input id="title" [(ngModel)]="model.title" maxlength="180" placeholder="e.g. Electricity bill" />
        <div class="two-fields">
          <div><label for="amount">Amount (₹) *</label><input id="amount" type="number" min="0.01" step="0.01" [(ngModel)]="model.amount" /></div>
          <div>
            <label for="category">Category</label>
            <select id="category" [(ngModel)]="model.category">
              @for (c of summary()?.categories ?? []; track c) {
                <option>{{ c }}</option>
              }
            </select>
          </div>
        </div>
        <div class="two-fields">
          <div><label for="date">Date</label><input id="date" type="date" [(ngModel)]="model.spentOn" /></div>
          <div>
            <label for="method">Method</label>
            <select id="method" [(ngModel)]="model.method">
              @for (m of methods; track m) {
                <option>{{ m }}</option>
              }
            </select>
          </div>
        </div>
        <label for="notes">Notes</label>
        <textarea id="notes" rows="2" maxlength="1200" [(ngModel)]="model.notes"></textarea>
        <div class="helper">Expenses cannot be edited afterwards. To correct one, void it and enter it again.</div>
      </app-modal>
    }

    @if (voidTarget(); as target) {
      <app-reason-dialog
        title="Void expense"
        [description]="target.title + ' stays on record, marked as voided, and is left out of all totals.'"
        confirmLabel="Void expense"
        [busy]="voiding.busy()"
        [error]="voiding.error()"
        (cancelled)="voidTarget.set(null)"
        (confirmed)="confirmVoid(target, $event)"
      />
    }
  `,
})
export class ExpensesComponent {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(Notify);

  protected readonly permission = Permission;
  protected readonly methods = PAYMENT_METHODS;
  protected readonly search = signal('');
  protected readonly category = signal('All');
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly summary = signal<ExpenseSummary | null>(null);
  protected readonly form = new FormState();
  protected model = this.blank();
  protected readonly voidTarget = signal<Expense | null>(null);
  protected readonly voiding = new FormState();
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly list = new PagedList<Expense>((page, pageSize) =>
    this.api.get<Paged<Expense>>('expenses', { page, pageSize, search: this.search(), category: this.category(), from: this.from(), to: this.to() }),
  );

  constructor() {
    this.reload();
  }

  protected can(permission: string): boolean {
    return this.auth.can(permission);
  }

  private reload(): void {
    this.list.load();
    this.api.get<ExpenseSummary>('expenses/summary').subscribe({ next: s => this.summary.set(s), error: e => this.list.error.set(errorMessage(e)) });
  }

  protected onSearch(text: string): void {
    this.search.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.list.reload(), 300);
  }

  private blank() {
    return { title: '', amount: 0, category: 'Other', spentOn: today(), method: 'Cash', notes: '' };
  }

  protected open(): void {
    this.model = this.blank();
    this.form.show();
  }

  protected save(): void {
    if (!this.model.title.trim()) return this.form.fail('Enter a title.');
    if (!(Number(this.model.amount) > 0)) return this.form.fail('Enter an amount greater than zero.');
    this.form.submit(this.api.post('expenses', this.model), () => {
      this.notify.success('Expense recorded');
      this.reload();
    });
  }

  protected confirmVoid(expense: Expense, reason: string): void {
    this.voiding.submit(this.api.post(`expenses/${expense.id}/void`, { reason }), () => {
      this.voidTarget.set(null);
      this.notify.success('Expense voided');
      this.reload();
    });
  }
}
