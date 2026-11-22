import { Component, inject, signal } from '@angular/core';
import { Api, FormState, Notify, PagedList, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { Expense, ExpenseSummary, PAYMENT_METHODS, Paged, Permission } from '../../core/models';
import { ProofFile, fileSize, readProofFile } from '../../shared/proof-files';
import { RecordHistoryComponent } from '../../shared/record-history';
import { UI, today } from '../../shared/ui';
import { ExpenseProofsComponent } from './expense-proofs';

@Component({
  selector: 'app-expenses',
  imports: [UI, ExpenseProofsComponent, RecordHistoryComponent],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Expenses</h1>
        <p>Operating spend, by category, with the bills that back it.</p>
      </div>
      <div class="header-actions">
        @if (can(permission.SettingsManage)) {
          <button type="button" class="secondary" (click)="openRule()">⚙ Proof rule</button>
        }
        @if (can(permission.ExpensesWrite)) {
          <button type="button" class="primary" (click)="open()">+ Add expense</button>
        }
      </div>
    </div>

    @if (summary()?.proofRequiredAbove != null) {
      <div class="info-strip">📎 A bill or receipt is required for every expense above {{ summary()!.proofRequiredAbove | money }}.</div>
    }

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
        <thead><tr><th>TITLE</th><th>CATEGORY</th><th>DATE</th><th>METHOD</th><th>AMOUNT</th><th>PROOF</th><th>STATUS</th><th></th></tr></thead>
        <tbody>
          @for (e of list.items(); track e.id) {
            <tr [class.dimmed]="e.status === 'Voided'">
              <td><strong>{{ e.title }}</strong><small>{{ e.status === 'Voided' ? 'Voided: ' + e.voidReason : e.notes }}</small></td>
              <td>{{ e.category }}</td>
              <td>{{ e.spentOn | day }}</td>
              <td>{{ e.method }}</td>
              <td class="negative">{{ e.amount | money: true }}</td>
              <td>
                @if (e.proofCount) {
                  <button type="button" class="proof-chip" (click)="proofTarget.set(e)" [title]="'View ' + e.proofCount + ' attached file(s)'">📎 {{ e.proofCount }}</button>
                } @else if (e.status === 'Posted' && can(permission.ExpensesWrite)) {
                  <button type="button" class="proof-chip missing" (click)="proofTarget.set(e)" title="Attach a bill">＋ Attach</button>
                } @else {
                  <span class="muted">—</span>
                }
              </td>
              <td><app-status [value]="e.status" /></td>
              <td class="row-actions">
                @if (can(permission.AuditRead)) {
                  <button type="button" class="table-action" (click)="historyTarget.set(e)">History</button>
                }
                @if (e.status === 'Posted' && can(permission.LedgerCorrect)) {
                  <button type="button" class="table-action danger-text" (click)="voidTarget.set(e)">Void</button>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="8" class="empty">{{ list.loading() ? 'Loading…' : 'No expenses found.' }}</td></tr>
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

        <label>Bill or receipt {{ proofRequired() ? '*' : '(optional)' }}</label>
        @for (p of proofs(); track $index) {
          <div class="proof-pending">
            <span>📎 {{ p.fileName }} <small>{{ size(p.size) }}</small></span>
            <button type="button" class="table-action danger-text" (click)="dropProof($index)">Remove</button>
          </div>
        }
        @if (proofs().length < (summary()?.maxProofs ?? 5)) {
          <label class="proof-drop compact">
            <input type="file" accept="image/*,application/pdf" multiple (change)="pickProofs($event)" [disabled]="reading()" />
            <span>{{ reading() ? 'Preparing…' : '＋ Take a photo or choose a file' }}</span>
            <small>Photo or PDF, up to {{ summary()?.maxProofMb ?? 5 }} MB each</small>
          </label>
        }
        @if (proofRequired()) {
          <div class="helper">This academy requires a bill for expenses above {{ summary()!.proofRequiredAbove | money }}.</div>
        }
        <div class="helper">Expenses cannot be edited afterwards. To correct one, void it and enter it again. Bills can be added later.</div>
      </app-modal>
    }

    @if (proofTarget(); as target) {
      <app-expense-proofs
        [expense]="target"
        [canWrite]="can(permission.ExpensesWrite)"
        [maxProofs]="summary()?.maxProofs ?? 5"
        [maxMb]="summary()?.maxProofMb ?? 5"
        (changed)="proofCountChanged(target, $event)"
        (closed)="proofTarget.set(null)"
      />
    }

    @if (historyTarget(); as target) {
      <app-record-history entityType="Expense" [entityId]="target.id" (closed)="historyTarget.set(null)" />
    }

    @if (ruleOpen()) {
      <app-modal title="Proof rule" [busy]="rule.busy()" [error]="rule.error()" (closed)="rule.close(); ruleOpen.set(false)" (saved)="saveRule()">
        <label class="check"><input type="checkbox" [(ngModel)]="ruleModel.required" /> Require a bill or receipt for larger expenses</label>
        @if (ruleModel.required) {
          <label for="rule-amount">Required above (₹)</label>
          <input id="rule-amount" type="number" min="0" step="1" [(ngModel)]="ruleModel.above" />
          <div class="helper">Expenses above this amount can't be saved without a photo or PDF of the bill, and their last bill can't be removed. Set 0 to require proof for every expense.</div>
        } @else {
          <div class="helper">Proof stays optional: staff can still attach bills, and the activity log flags any bill that is removed.</div>
        }
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
  protected readonly proofs = signal<ProofFile[]>([]);
  protected readonly reading = signal(false);
  protected readonly proofTarget = signal<Expense | null>(null);
  protected readonly historyTarget = signal<Expense | null>(null);
  protected readonly ruleOpen = signal(false);
  protected readonly rule = new FormState();
  protected ruleModel = { required: false, above: 2000 };
  protected readonly size = fileSize;
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
    this.proofs.set([]);
    this.form.show();
  }

  /** True when the amount entered needs a bill under the academy's rule. */
  protected proofRequired(): boolean {
    const above = this.summary()?.proofRequiredAbove;
    return above != null && Number(this.model.amount) > above;
  }

  protected async pickProofs(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const room = (this.summary()?.maxProofs ?? 5) - this.proofs().length;
    const chosen = Array.from(input.files ?? []).slice(0, room);
    input.value = '';
    this.reading.set(true);
    try {
      for (const file of chosen) {
        const proof = await readProofFile(file, this.summary()?.maxProofMb ?? 5);
        this.proofs.update(list => [...list, proof]);
      }
    } catch (e) {
      this.form.fail(e instanceof Error ? e.message : 'The file could not be read.');
    } finally {
      this.reading.set(false);
    }
  }

  protected dropProof(index: number): void {
    this.proofs.update(list => list.filter((_, i) => i !== index));
  }

  protected proofCountChanged(expense: Expense, count: number): void {
    if (expense.proofCount === count) return;
    this.list.items.update(items => items.map(e => (e.id === expense.id ? { ...e, proofCount: count } : e)));
  }

  protected openRule(): void {
    const above = this.summary()?.proofRequiredAbove;
    this.ruleModel = { required: above != null, above: above ?? 2000 };
    this.rule.show();
    this.ruleOpen.set(true);
  }

  protected saveRule(): void {
    if (this.ruleModel.required && !(Number(this.ruleModel.above) >= 0)) return this.rule.fail('Enter an amount of zero or more.');
    const body = { proofRequiredAbove: this.ruleModel.required ? Number(this.ruleModel.above) : null };
    this.rule.submit(this.api.put('expenses/settings', body), () => {
      this.ruleOpen.set(false);
      this.notify.success(body.proofRequiredAbove == null ? 'Proof is now optional' : 'Proof rule saved');
      this.reload();
    });
  }

  protected save(): void {
    if (!this.model.title.trim()) return this.form.fail('Enter a title.');
    if (!(Number(this.model.amount) > 0)) return this.form.fail('Enter an amount greater than zero.');
    if (this.reading()) return this.form.fail('Wait for the files to finish preparing.');
    if (this.proofRequired() && !this.proofs().length)
      return this.form.fail(`Attach a bill or receipt: it is required above ₹${this.summary()!.proofRequiredAbove}.`);
    const body = { ...this.model, proofs: this.proofs().map(p => ({ fileName: p.fileName, dataUrl: p.dataUrl })) };
    this.form.submit(this.api.post('expenses', body), () => {
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
