import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, Notify, errorMessage } from '../../core/api';
import { PlatformSummary } from '../../core/models';
import { UI } from '../../shared/ui';

@Component({
  selector: 'app-platform-overview',
  imports: [RouterLink, UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">PLATFORM</div>
        <h1>Overview</h1>
        <p>Your academies and subscription revenue.</p>
      </div>
      <div class="page-actions">
        <button type="button" class="secondary" [disabled]="running()" (click)="runBilling()">↻ Run billing now</button>
        <a class="primary" routerLink="/platform/academies" [queryParams]="{ new: 1 }">+ New academy</a>
      </div>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (summary(); as s) {
      <div class="stats four">
        <article class="stat purple"><div class="stat-top">MONTHLY RECURRING REVENUE</div><div class="stat-value">{{ s.monthlyRecurringRevenue | money }}</div><small>{{ s.payingSubscriptions }} paying academies</small></article>
        <article class="stat green"><div class="stat-top">COLLECTED THIS MONTH</div><div class="stat-value">{{ s.collectedThisMonth | money }}</div></article>
        <article class="stat yellow"><div class="stat-top">UNPAID INVOICES</div><div class="stat-value">{{ s.openInvoiceAmount | money }}</div><small>{{ s.openInvoices }} open · {{ s.overdueInvoices }} overdue</small></article>
        <article class="stat white"><div class="stat-top">ACADEMIES</div><div class="stat-value">{{ s.totalTenants }}</div><small>{{ s.activeTenants }} active · {{ s.suspendedTenants }} suspended</small></article>
      </div>

      <section class="surface">
        <div class="surface-title"><h2>How billing runs</h2></div>
        <p class="muted">
          A renewal invoice is raised automatically a few days before each academy's period ends and emailed to its Super Admins.
          Paying it — online, or marked paid by you — extends the academy by one billing period. An academy that has not paid
          keeps working for a short grace period, then is locked until the invoice is settled. The check runs hourly;
          "Run billing now" does it immediately.
        </p>
        <div class="button-row">
          <a class="secondary" routerLink="/platform/invoices">View invoices</a>
          <a class="secondary" routerLink="/platform/plans">Manage plans</a>
        </div>
      </section>
    }
  `,
})
export class PlatformOverviewComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  protected readonly summary = signal<PlatformSummary | null>(null);
  protected readonly error = signal('');
  protected readonly running = signal(false);

  constructor() {
    this.load();
  }

  private load(): void {
    this.api.get<PlatformSummary>('platform/summary').subscribe({ next: s => this.summary.set(s), error: e => this.error.set(errorMessage(e)) });
  }

  protected runBilling(): void {
    this.running.set(true);
    this.api.post<{ invoicesCreated: number; markedPastDue: number }>('platform/billing/run').subscribe({
      next: result => {
        this.running.set(false);
        this.notify.success(`Billing run complete: ${result.invoicesCreated} invoice(s) raised, ${result.markedPastDue} subscription(s) marked past due`);
        this.load();
      },
      error: e => {
        this.running.set(false);
        this.error.set(errorMessage(e));
      },
    });
  }
}
