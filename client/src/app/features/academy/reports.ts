import { Component, computed, inject, signal } from '@angular/core';
import { Api, apiUrl, errorMessage } from '../../core/api';
import { ReportSummary } from '../../core/models';
import { UI } from '../../shared/ui';

const EXPORTS = [
  { kind: 'payments', label: 'Payments and refunds' },
  { kind: 'expenses', label: 'Expenses' },
  { kind: 'payouts', label: 'Faculty payouts' },
  { kind: 'students', label: 'Student fee status' },
] as const;

@Component({
  selector: 'app-reports',
  imports: [UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Reports</h1>
        <p>Money in and out for a period, and what is still owed.</p>
      </div>
    </div>

    <div class="filter-bar">
      <label class="inline">From <input type="date" [ngModel]="from()" (ngModelChange)="from.set($event); load()" /></label>
      <label class="inline">To <input type="date" [ngModel]="to()" (ngModelChange)="to.set($event); load()" /></label>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (report(); as r) {
      <p class="muted">Showing {{ r.from | day }} to {{ r.to | day }}. Voided entries are excluded.</p>
      <div class="stats four">
        <article class="stat green"><div class="stat-top">NET COLLECTIONS</div><div class="stat-value">{{ r.netCollections | money }}</div><small>{{ r.refunds | money }} refunded</small></article>
        <article class="stat pink"><div class="stat-top">EXPENSES</div><div class="stat-value">{{ r.expenses | money }}</div></article>
        <article class="stat amber"><div class="stat-top">FACULTY PAYOUTS</div><div class="stat-value">{{ r.payouts | money }}</div></article>
        <article class="stat purple"><div class="stat-top">NET CASH</div><div class="stat-value">{{ r.netCash | money }}</div><small>Cash basis, not accounting profit</small></article>
      </div>

      <section class="surface">
        <div class="surface-title"><h2>Month by month</h2></div>
        <div class="table-scroll">
          <table>
            <thead><tr><th>MONTH</th><th>COLLECTIONS</th><th>EXPENSES</th><th>PAYOUTS</th><th>NET CASH</th><th></th></tr></thead>
            <tbody>
              @for (m of r.months; track m.month) {
                <tr>
                  <td><strong>{{ m.month }}</strong></td>
                  <td class="positive">{{ m.collections | money }}</td>
                  <td>{{ m.expenses | money }}</td>
                  <td>{{ m.payouts | money }}</td>
                  <td [class.negative]="m.net < 0">{{ m.net | money }}</td>
                  <td class="spark"><span [style.width.%]="(100 * m.collections) / highest()"></span></td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      <div class="dashboard-grid even">
        <section class="surface">
          <div class="surface-title"><h2>Collections by method</h2></div>
          @for (m of r.byMethod; track m.method) {
            <div class="performance-line"><div><strong>{{ m.method }}</strong><small>{{ m.count }} payments</small></div><b>{{ m.amount | money }}</b></div>
          } @empty {
            <p class="empty">No payments in this period.</p>
          }
        </section>
        <section class="surface">
          <div class="surface-title"><h2>Expenses by category</h2></div>
          @for (c of r.expensesByCategory; track c.category) {
            <div class="performance-line"><div><strong>{{ c.category }}</strong><small>{{ c.count }} entries</small></div><b>{{ c.amount | money }}</b></div>
          } @empty {
            <p class="empty">No expenses in this period.</p>
          }
        </section>
      </div>

      <section class="surface">
        <div class="surface-title"><h2>Fees outstanding by course</h2><span>AS OF TODAY</span></div>
        <div class="table-scroll">
          <table>
            <thead><tr><th>COURSE</th><th>STUDENTS</th><th>PAYABLE</th><th>COLLECTED</th><th>PENDING</th></tr></thead>
            <tbody>
              @for (c of r.courses; track c.courseName) {
                <tr>
                  <td><strong>{{ c.courseName }}</strong></td><td>{{ c.students }}</td><td>{{ c.payable | money }}</td>
                  <td class="positive">{{ c.collected | money }}</td><td class="negative">{{ c.pending | money }}</td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="empty">No courses yet.</td></tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      <section class="surface">
        <div class="surface-title"><h2>Download for Excel</h2></div>
        <p class="muted">CSV files for the period above. Student fee status is always as of today.</p>
        <div class="button-row">
          @for (item of exports; track item.kind) {
            <a class="secondary" [href]="exportUrl(item.kind)" download>⤓ {{ item.label }}</a>
          }
        </div>
      </section>
    }
  `,
})
export class ReportsComponent {
  private readonly api = inject(Api);
  protected readonly exports = EXPORTS;
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly report = signal<ReportSummary | null>(null);
  protected readonly error = signal('');
  protected readonly highest = computed(() => Math.max(1, ...(this.report()?.months.map(m => m.collections) ?? [])));

  constructor() {
    this.load();
  }

  protected load(): void {
    this.api.get<ReportSummary>('reports/summary', { from: this.from(), to: this.to() }).subscribe({
      next: report => {
        this.error.set('');
        this.report.set(report);
      },
      error: e => this.error.set(errorMessage(e)),
    });
  }

  protected exportUrl(kind: string): string {
    return apiUrl(`reports/export/${kind}`, { from: this.from(), to: this.to() });
  }
}
