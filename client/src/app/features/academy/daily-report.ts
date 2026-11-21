import { Component, computed, inject, signal } from '@angular/core';
import { Api, apiUrl, errorMessage } from '../../core/api';
import { ActivityEvent, ActivityReport, ReportGrouping } from '../../core/models';
import { UI, today } from '../../shared/ui';
import { GROUPINGS } from './reports';

/** yyyy-MM-dd for a local date. */
function iso(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function daysAgo(n: number): string {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return iso(d);
}

const PRESETS: { key: string; label: string; range: () => [string, string] }[] = [
  { key: 'today', label: 'Today', range: () => [today(), today()] },
  { key: 'yesterday', label: 'Yesterday', range: () => [daysAgo(1), daysAgo(1)] },
  {
    key: 'week',
    label: 'This week',
    range: () => {
      const d = new Date();
      d.setDate(d.getDate() - ((d.getDay() + 6) % 7));
      return [iso(d), today()];
    },
  },
  {
    key: 'month',
    label: 'This month',
    range: () => {
      const d = new Date();
      return [iso(new Date(d.getFullYear(), d.getMonth(), 1)), today()];
    },
  },
  { key: '30', label: 'Last 30 days', range: () => [daysAgo(29), today()] },
];

/**
 * Everything that happened in the academy over a date range, day by day, week by week or month by month:
 * admissions, applications, fees, refunds, discounts, expenses, faculty pay, teaching time and tests.
 */
@Component({
  selector: 'app-daily-report',
  imports: [UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">SUPER ADMIN</div>
        <h1>Daily report</h1>
        <p>What happened in the academy, by day, week or month. Download it as an Excel workbook with the same grouping.</p>
      </div>
      <div class="page-actions">
        <a class="primary" [href]="excelUrl()" download>⤓ Download Excel</a>
      </div>
    </div>

    <div class="filter-bar">
      <label class="inline">From <input type="date" [max]="to()" [ngModel]="from()" (ngModelChange)="from.set($event); preset.set(''); load()" /></label>
      <label class="inline">To <input type="date" [min]="from()" [ngModel]="to()" (ngModelChange)="to.set($event); preset.set(''); load()" /></label>
      <div class="group-switch" role="group" aria-label="Group by">
        @for (g of groupings; track g.value) {
          <button type="button" [class.active]="groupBy() === g.value" (click)="groupBy.set(g.value); load()">{{ g.label }}</button>
        }
      </div>
    </div>
    <div class="preset-row gap-bottom">
      @for (p of presets; track p.key) {
        <button type="button" [class.active]="preset() === p.key" (click)="usePreset(p.key)">{{ p.label }}</button>
      }
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (report(); as r) {
      <div class="stats four">
        <article class="stat purple">
          <div class="stat-top">ADMISSIONS</div>
          <div class="stat-value">{{ r.totals.admissions }}</div>
          <small>{{ r.totals.applications }} online {{ r.totals.applications === 1 ? 'application' : 'applications' }}</small>
        </article>
        <article class="stat green">
          <div class="stat-top">FEES COLLECTED</div>
          <div class="stat-value">{{ r.totals.collections | money }}</div>
          <small>{{ r.totals.refunds | money }} refunded · {{ r.totals.discounts | money }} discounts</small>
        </article>
        <article class="stat pink">
          <div class="stat-top">MONEY OUT</div>
          <div class="stat-value">{{ r.totals.expenses + r.totals.payouts | money }}</div>
          <small>{{ r.totals.expenses | money }} expenses · {{ r.totals.payouts | money }} faculty</small>
        </article>
        <article class="stat amber">
          <div class="stat-top">NET CASH</div>
          <div class="stat-value" [class.negative]="r.totals.netCash < 0">{{ r.totals.netCash | money }}</div>
          <small>{{ r.totals.teachingMinutes }} min taught · {{ r.totals.tests }} {{ r.totals.tests === 1 ? 'test' : 'tests' }}</small>
        </article>
      </div>

      <section class="surface">
        <div class="surface-title"><h2>{{ r.title }}</h2><small class="muted">{{ r.from | day }} – {{ r.to | day }}</small></div>
        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>{{ r.groupBy === 'Day' ? 'DAY' : r.groupBy === 'Week' ? 'WEEK' : 'MONTH' }}</th><th>ADMISSIONS</th><th>APPLICATIONS</th>
                <th>COLLECTED</th><th>REFUNDS</th><th>EXPENSES</th><th>FACULTY PAID</th><th>TEACHING</th><th>TESTS</th><th>NET CASH</th>
              </tr>
            </thead>
            <tbody>
              @for (p of r.periods; track p.from) {
                <tr [class.dimmed]="isQuiet(p.totals)">
                  <td><strong>{{ p.label }}</strong></td>
                  <td>{{ p.totals.admissions || '—' }}</td>
                  <td>{{ p.totals.applications || '—' }}</td>
                  <td class="positive">{{ p.totals.collections | money }}</td>
                  <td>{{ p.totals.refunds | money }}</td>
                  <td>{{ p.totals.expenses | money }}</td>
                  <td>{{ p.totals.payouts | money }}</td>
                  <td>{{ p.totals.teachingMinutes ? p.totals.teachingMinutes + ' min' : '—' }}</td>
                  <td>{{ p.totals.tests || '—' }}</td>
                  <td [class.negative]="p.totals.netCash < 0">{{ p.totals.netCash | money }}</td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr>
                <th>TOTAL</th><th>{{ r.totals.admissions }}</th><th>{{ r.totals.applications }}</th><th>{{ r.totals.collections | money }}</th>
                <th>{{ r.totals.refunds | money }}</th><th>{{ r.totals.expenses | money }}</th><th>{{ r.totals.payouts | money }}</th>
                <th>{{ r.totals.teachingMinutes }} min</th><th>{{ r.totals.tests }}</th><th>{{ r.totals.netCash | money }}</th>
              </tr>
            </tfoot>
          </table>
        </div>
      </section>

      <section class="surface gap-top">
        <div class="surface-title">
          <h2>Everything that happened</h2>
          <small class="muted">{{ r.eventCount }} {{ r.eventCount === 1 ? 'entry' : 'entries' }}, newest first</small>
        </div>
        <div class="preset-row gap-bottom">
          <button type="button" [class.active]="type() === ''" (click)="type.set('')">All</button>
          @for (t of types(); track t.type) {
            <button type="button" [class.active]="type() === t.type" (click)="type.set(t.type)">{{ t.type }} · {{ t.count }}</button>
          }
        </div>
        <div class="table-scroll">
          <table>
            <thead><tr><th>DATE</th><th>TYPE</th><th>NAME</th><th>DETAILS</th><th>AMOUNT</th><th>STATUS</th><th>BY</th></tr></thead>
            <tbody>
              @for (e of shown(); track $index) {
                <tr [class.dimmed]="e.status === 'Voided' || e.status === 'Withdrawn' || e.status === 'Rejected'">
                  <td>{{ e.date | day }}<small>{{ time(e.enteredAt) }}</small></td>
                  <td><span class="event-type" [class.in]="e.flow === 'In'" [class.out]="e.flow === 'Out'">{{ e.type }}</span></td>
                  <td><strong>{{ e.title }}</strong></td>
                  <td><small>{{ e.detail || '—' }}</small></td>
                  <td>{{ e.amount === null ? '—' : (e.amount | money: true) }}</td>
                  <td>
                    @if (e.status) {
                      <app-status [value]="e.status" />
                    }
                  </td>
                  <td><small>{{ e.by || '—' }}</small></td>
                </tr>
              } @empty {
                <tr><td colspan="7" class="empty">Nothing was recorded in this period.</td></tr>
              }
            </tbody>
          </table>
        </div>
        @if (r.truncated) {
          <p class="muted">Showing the latest 1,000 entries. The Excel download has all of them.</p>
        }
      </section>
    }
  `,
})
export class DailyReportComponent {
  private readonly api = inject(Api);
  protected readonly groupings = GROUPINGS;
  protected readonly presets = PRESETS;
  protected readonly from = signal(today());
  protected readonly to = signal(today());
  protected readonly preset = signal('today');
  protected readonly groupBy = signal<ReportGrouping>('Day');
  protected readonly type = signal('');
  protected readonly report = signal<ActivityReport | null>(null);
  protected readonly error = signal('');

  protected readonly types = computed(() => {
    const counts = new Map<string, number>();
    for (const e of this.report()?.events ?? []) counts.set(e.type, (counts.get(e.type) ?? 0) + 1);
    return [...counts].map(([type, count]) => ({ type, count }));
  });
  protected readonly shown = computed<ActivityEvent[]>(() => {
    const events = this.report()?.events ?? [];
    return this.type() ? events.filter(e => e.type === this.type()) : events;
  });

  constructor() {
    this.load();
  }

  protected usePreset(key: string): void {
    const [from, to] = PRESETS.find(p => p.key === key)!.range();
    this.from.set(from);
    this.to.set(to);
    this.preset.set(key);
    this.load();
  }

  protected load(): void {
    this.api.get<ActivityReport>('reports/activity', { from: this.from(), to: this.to(), groupBy: this.groupBy() }).subscribe({
      next: r => {
        this.error.set('');
        this.report.set(r);
        if (this.type() && !r.events.some(e => e.type === this.type())) this.type.set('');
      },
      error: e => {
        this.report.set(null);
        this.error.set(errorMessage(e));
      },
    });
  }

  protected excelUrl(): string {
    return apiUrl('reports/activity/excel', { from: this.from(), to: this.to(), groupBy: this.groupBy() });
  }

  /** "2026-11-21T14:05:12" (Indian time, no zone) → "2:05 pm". */
  protected time(value: string): string {
    const match = /T(\d{2}):(\d{2})/.exec(value);
    if (!match) return '';
    const h = Number(match[1]);
    return `${((h + 11) % 12) + 1}:${match[2]} ${h < 12 ? 'am' : 'pm'}`;
  }

  protected isQuiet(t: ActivityReport['totals']): boolean {
    return !t.admissions && !t.applications && !t.collections && !t.refunds && !t.expenses && !t.payouts && !t.teachingMinutes && !t.tests;
  }
}
