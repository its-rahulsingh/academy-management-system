import { Component, computed, inject, signal } from '@angular/core';
import { Api, FormState, Notify, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { ActivityDashboard, ActivityFlag, Permission } from '../../core/models';
import { entityLabel } from '../../shared/audit-format';
import { RecordHistoryComponent } from '../../shared/record-history';
import { UI, today } from '../../shared/ui';

const DAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

function shiftDays(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/**
 * The activity log as a dashboard: headline numbers, what needs attention, activity over time,
 * the busiest hours and who did what. Each flag links to the history of the record it is about.
 */
@Component({
  selector: 'app-activity-dashboard',
  imports: [UI, RecordHistoryComponent],
  template: `
    <div class="filter-bar activity-filters">
      <div class="segmented">
        @for (r of ranges; track r.label) {
          <button type="button" [class.active]="rangeLabel() === r.label" (click)="pickRange(r.label)">{{ r.label }}</button>
        }
      </div>
      <input type="date" [ngModel]="from()" (ngModelChange)="from.set($event); rangeLabel.set(''); load()" aria-label="From date" />
      <input type="date" [ngModel]="to()" (ngModelChange)="to.set($event); rangeLabel.set(''); load()" aria-label="To date" />
      <select [ngModel]="groupBy()" (ngModelChange)="groupBy.set($event); load()" aria-label="Group by">
        <option value="Day">Day by day</option>
        <option value="Week">Week by week</option>
        <option value="Month">Month by month</option>
      </select>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (data(); as d) {
      @if (d.truncated) {
        <div class="info-strip">This range has a very large number of entries; only the first 100,000 are counted. Choose a shorter range for exact figures.</div>
      }

      <div class="stats four">
        <article class="stat purple">
          <div class="stat-top">TOTAL ACTIONS</div>
          <div class="stat-value">{{ d.cards.actions }}</div>
          <small [class.trend-up]="trend() > 0" [class.trend-down]="trend() < 0">{{ trendText() }}</small>
        </article>
        <article class="stat white">
          <div class="stat-top">ACTIVE USERS</div>
          <div class="stat-value">{{ d.cards.activeUsers }}</div>
          <small>{{ d.cards.signIns }} sign-ins</small>
        </article>
        <article class="stat amber">
          <div class="stat-top">MONEY EDITS ⚠</div>
          <div class="stat-value">{{ d.cards.moneyEdits }}</div>
          <small>Cancellations, refunds, discounts, fee changes</small>
        </article>
        <article class="stat pink">
          <div class="stat-top">DELETED / CANCELLED</div>
          <div class="stat-value">{{ d.cards.removals }}</div>
          <small>Records removed or voided</small>
        </article>
        <article class="stat white">
          <div class="stat-top">AFTER HOURS</div>
          <div class="stat-value">{{ d.cards.afterHours }}</div>
          <small>Actions between 10 PM and 6 AM</small>
        </article>
        <article class="stat white">
          <div class="stat-top">FAILED SIGN-INS</div>
          <div class="stat-value">{{ d.cards.failedSignIns }}</div>
          <small>Wrong passwords entered</small>
        </article>
        <article class="stat rose">
          <div class="stat-top">NEEDS ATTENTION</div>
          <div class="stat-value">{{ openFlags() }}</div>
          <small>{{ d.flagCounts.red }} red · {{ d.flagCounts.orange }} orange · {{ d.flagCounts.yellow }} yellow</small>
        </article>
        <article class="stat green">
          <div class="stat-top">REVIEWED</div>
          <div class="stat-value">{{ d.flagCounts.reviewed }}</div>
          <small>Flags marked as fine</small>
        </article>
      </div>

      <section class="surface">
        <div class="surface-title">
          <h2>Needs attention</h2>
          <div class="flag-tools">
            <label class="check"><input type="checkbox" [ngModel]="showReviewed()" (ngModelChange)="showReviewed.set($event); load()" /> Show reviewed</label>
            @if (can(permission.SettingsManage)) {
              <button type="button" class="table-action" (click)="openAlert()">Large amount: {{ d.largeAmountAlert | money }} ✎</button>
            }
          </div>
        </div>
        <div class="flag-list">
          @for (f of d.flags; track f.key) {
            <article class="flag" [class]="'flag ' + f.severity" [class.reviewed]="f.reviewed">
              <span class="flag-dot" [attr.aria-label]="f.severity + ' flag'"></span>
              <div class="flag-body">
                <strong>{{ f.title }}</strong>
                <p>{{ f.detail }}</p>
                <small>
                  {{ f.userName || 'System' }} · {{ time(f.atUtc) }}
                  @if (f.reviewed) {
                    · ✓ Reviewed by {{ f.reviewedBy }}
                  }
                </small>
              </div>
              <div class="flag-actions">
                @if (f.entityType && f.entityId) {
                  <button type="button" class="table-action" (click)="history.set({ type: f.entityType, id: f.entityId })">History</button>
                }
                @if (can(permission.ReportsActivity)) {
                  <button type="button" class="table-action" [disabled]="reviewing() === f.key" (click)="review(f)">{{ f.reviewed ? 'Reopen' : '✓ Mark reviewed' }}</button>
                }
              </div>
            </article>
          } @empty {
            <div class="flag-empty">✓ Nothing needs attention in this period.</div>
          }
        </div>
      </section>

      <section class="surface">
        <div class="surface-title">
          <h2>Activity over time</h2>
          <div class="chart-legend">
            <span><i class="created"></i>Created</span><span><i class="changed"></i>Changed</span><span><i class="removed"></i>Deleted / cancelled</span>
          </div>
        </div>
        <div class="timeline" [class.dense]="d.timeline.length > 16">
          @for (p of d.timeline; track p.from; let i = $index) {
            <div class="timeline-col" [title]="p.label + ': ' + p.total + ' actions (' + p.created + ' created, ' + p.changed + ' changed, ' + p.removed + ' removed)'">
              <div class="timeline-bar">
                <span class="removed" [style.height.%]="pct(p.removed, maxPeriod())"></span>
                <span class="changed" [style.height.%]="pct(p.changed, maxPeriod())"></span>
                <span class="created" [style.height.%]="pct(p.created, maxPeriod())"></span>
              </div>
              <small>{{ showTick(i, d.timeline.length) ? shortLabel(p.label) : '' }}</small>
            </div>
          }
        </div>
      </section>

      <div class="activity-grid">
        <section class="surface">
          <div class="surface-title"><h2>Busiest hours</h2><span>ACADEMY TIME</span></div>
          <div class="heatmap" role="table" aria-label="Actions by day of week and hour">
            <div class="heat-row heat-hours" role="row">
              <span></span>
              @for (h of hours; track h) {
                <small>{{ h % 3 === 0 ? hourLabel(h) : '' }}</small>
              }
            </div>
            @for (row of d.heatmap; track $index; let day = $index) {
              <div class="heat-row" role="row">
                <span class="heat-day">{{ days[day] }}</span>
                @for (count of row; track $index; let h = $index) {
                  <i class="heat-cell" [class.night]="h >= 22 || h < 6" [style.background]="heat(count)" [title]="days[day] + ' ' + hourLabel(h) + ': ' + count + ' actions'"></i>
                }
              </div>
            }
          </div>
          <p class="helper">Darker means busier. Outlined cells are night hours (10 PM – 6 AM).</p>
        </section>

        <section class="surface">
          <div class="surface-title"><h2>What was worked on</h2></div>
          @for (a of d.byArea.slice(0, 8); track a.entityType) {
            <div class="hbar-row">
              <span class="hbar-label">{{ label(a.entityType) }}</span>
              <div class="hbar"><span class="changed" [style.width.%]="pct(a.count, d.byArea[0].count)"></span></div>
              <b>{{ a.count }}</b>
            </div>
          } @empty {
            <p class="helper">No activity in this period.</p>
          }
        </section>
      </div>

      <section class="surface">
        <div class="surface-title">
          <h2>Activity by user</h2>
          <div class="chart-legend">
            <span><i class="created"></i>Created</span><span><i class="changed"></i>Changed</span><span><i class="removed"></i>Deleted / cancelled</span>
          </div>
        </div>
        @for (u of d.byUser; track u.name) {
          <div class="hbar-row user">
            <span class="hbar-label"><strong>{{ u.name }}</strong><small>{{ role(u.role) }}{{ u.afterHours ? ' · ' + u.afterHours + ' after hours' : '' }}</small></span>
            <div class="hbar">
              <span class="created" [style.width.%]="pct(u.created, d.byUser[0].total)" [title]="u.created + ' created'"></span>
              <span class="changed" [style.width.%]="pct(u.changed, d.byUser[0].total)" [title]="u.changed + ' changed'"></span>
              <span class="removed" [style.width.%]="pct(u.removed, d.byUser[0].total)" [title]="u.removed + ' deleted or cancelled'"></span>
            </div>
            <b>{{ u.total }}</b>
          </div>
        } @empty {
          <p class="helper">No activity in this period.</p>
        }
      </section>
    } @else if (!error()) {
      <p class="helper">Loading…</p>
    }

    @if (history(); as h) {
      <app-record-history [entityType]="h.type" [entityId]="h.id" (closed)="history.set(null)" />
    }

    @if (alertForm.open()) {
      <app-modal title="Large amount alert" [busy]="alertForm.busy()" [error]="alertForm.error()" (closed)="alertForm.close()" (saved)="saveAlert()">
        <label for="alert-amount">Flag in red at or above (₹)</label>
        <input id="alert-amount" type="number" min="1" step="1" [(ngModel)]="alertAmount" />
        <div class="helper">Discounts and balance write-offs of this amount or more show as red flags. Smaller ones given by anyone other than the Super Admin show in orange.</div>
      </app-modal>
    }
  `,
})
export class ActivityDashboardComponent {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(Notify);
  protected readonly permission = Permission;
  protected readonly days = DAYS;
  protected readonly hours = Array.from({ length: 24 }, (_, i) => i);
  protected readonly ranges = [
    { label: 'Today', days: 0 },
    { label: '7 days', days: 6 },
    { label: '30 days', days: 29 },
    { label: '90 days', days: 89 },
  ];

  protected readonly rangeLabel = signal('30 days');
  protected readonly to = signal(today());
  protected readonly from = signal(shiftDays(today(), -29));
  protected readonly groupBy = signal<'Day' | 'Week' | 'Month'>('Day');
  protected readonly showReviewed = signal(false);
  protected readonly data = signal<ActivityDashboard | null>(null);
  protected readonly error = signal('');
  protected readonly reviewing = signal('');
  protected readonly history = signal<{ type: string; id: string } | null>(null);
  protected readonly alertForm = new FormState();
  protected alertAmount = 5000;

  protected readonly openFlags = computed(() => {
    const c = this.data()?.flagCounts;
    return c ? c.red + c.orange + c.yellow : 0;
  });
  protected readonly maxPeriod = computed(() => Math.max(1, ...(this.data()?.timeline.map(p => p.total) ?? [0])));
  private readonly maxHour = computed(() => Math.max(1, ...(this.data()?.heatmap.flat() ?? [0])));
  protected readonly trend = computed(() => {
    const c = this.data()?.cards;
    if (!c || !c.previousActions) return 0;
    return Math.round(((c.actions - c.previousActions) / c.previousActions) * 100);
  });
  protected readonly trendText = computed(() => {
    const c = this.data()?.cards;
    if (!c) return '';
    if (!c.previousActions) return c.actions ? 'Nothing in the previous period' : 'No activity';
    const t = this.trend();
    return t === 0 ? 'Same as the previous period' : `${t > 0 ? '▲' : '▼'} ${Math.abs(t)}% vs previous period`;
  });

  constructor() {
    this.load();
  }

  protected can(permission: string): boolean {
    return this.auth.can(permission);
  }

  protected pickRange(label: string): void {
    const range = this.ranges.find(r => r.label === label)!;
    this.rangeLabel.set(label);
    this.to.set(today());
    this.from.set(shiftDays(today(), -range.days));
    if (range.days >= 89 && this.groupBy() === 'Day') this.groupBy.set('Week');
    if (range.days < 89 && this.groupBy() !== 'Day') this.groupBy.set('Day');
    this.load();
  }

  protected load(): void {
    this.error.set('');
    this.api
      .get<ActivityDashboard>('audit/dashboard', { from: this.from(), to: this.to(), groupBy: this.groupBy(), showReviewed: this.showReviewed() })
      .subscribe({ next: d => this.data.set(d), error: e => this.error.set(errorMessage(e)) });
  }

  protected review(flag: ActivityFlag): void {
    this.reviewing.set(flag.key);
    this.api.post('audit/flags/review', { key: flag.key, reviewed: !flag.reviewed }).subscribe({
      next: () => {
        this.reviewing.set('');
        this.notify.success(flag.reviewed ? 'Flag reopened' : 'Marked as reviewed');
        this.load();
      },
      error: e => {
        this.reviewing.set('');
        this.error.set(errorMessage(e));
      },
    });
  }

  protected openAlert(): void {
    this.alertAmount = this.data()?.largeAmountAlert ?? 5000;
    this.alertForm.show();
  }

  protected saveAlert(): void {
    if (!(Number(this.alertAmount) > 0)) return this.alertForm.fail('Enter an amount greater than zero.');
    this.alertForm.submit(this.api.put('audit/settings', { largeAmountAlert: Number(this.alertAmount) }), () => {
      this.notify.success('Alert amount saved');
      this.load();
    });
  }

  protected pct(value: number, max: number): number {
    return max ? (value / max) * 100 : 0;
  }

  protected heat(count: number): string {
    if (!count) return '#f3f4f9';
    const strength = 0.15 + 0.85 * (count / this.maxHour());
    return `rgba(98, 85, 237, ${strength.toFixed(2)})`;
  }

  protected hourLabel(h: number): string {
    return h === 0 ? '12a' : h < 12 ? `${h}a` : h === 12 ? '12p' : `${h - 12}p`;
  }

  /** Every label when there are few bars, otherwise about eight evenly spaced ones. */
  protected showTick(index: number, count: number): boolean {
    if (count <= 16) return true;
    const step = Math.ceil(count / 8);
    if (index === count - 1) return true;
    // Leave out a tick that would crowd the last one.
    return index % step === 0 && count - 1 - index >= step / 2;
  }

  protected shortLabel(label: string): string {
    // "Mon, 02 Nov 2026" → "02 Nov"; "02–08 Nov 2026" → "02–08 Nov"; "November 2026" → "Nov 26"
    const day = /^\w{3}, (\d{2} \w{3})/.exec(label);
    if (day) return day[1];
    const month = /^(\w{3})\w* (\d{2})(\d{2})$/.exec(label);
    if (month) return `${month[1]} ${month[3]}`;
    return label.replace(/ \d{4}$/, '');
  }

  protected label = entityLabel;

  protected role(role: string | null): string {
    return role === 'SuperAdmin' ? 'Super Admin' : (role ?? '');
  }

  protected time(value: string): string {
    return new Date(value).toLocaleString('en-IN', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
  }
}
