/* eslint-disable @typescript-eslint/no-explicit-any -- the analytics payloads are wide, read-only report shapes */
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, FormState, Notify, errorMessage } from '../../core/api';
import { UI, today } from '../../shared/ui';

type Tab = 'business' | 'health' | 'usage' | 'load' | 'capacity';
const DAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

function shiftDays(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/**
 * The owner's analytics: business growth, academy health, feature usage, peak hours and speed, capacity and cost.
 * Counts, times and totals per academy only; no academy's own records.
 */
@Component({
  selector: 'app-platform-analytics',
  imports: [UI, RouterLink],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">PLATFORM</div>
        <h1>Analytics</h1>
        <p>How the platform is growing and being used. Counts, times and totals per academy; no academy's own records.</p>
      </div>
    </div>

    <div class="tabs" role="tablist">
      @for (t of tabs; track t.id) {
        <button type="button" role="tab" [class.active]="tab() === t.id" [attr.aria-selected]="tab() === t.id" (click)="open(t.id)">{{ t.label }}</button>
      }
    </div>

    @if (tab() === 'usage' || tab() === 'load') {
      <div class="filter-bar activity-filters">
        <div class="segmented">
          @for (r of ranges; track r.days) {
            <button type="button" [class.active]="rangeDays() === r.days" (click)="pickRange(r.days)">{{ r.label }}</button>
          }
        </div>
        @if (tab() === 'load') {
          <select [ngModel]="academyId()" (ngModelChange)="academyId.set($event); load()" aria-label="Academy">
            <option value="">All academies</option>
            @for (a of academyOptions(); track a.tenantId) {
              <option [value]="a.tenantId">{{ a.academy }}</option>
            }
          </select>
        }
      </div>
    }

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (!current() && !error()) {
      <p class="helper">Loading…</p>
    }

    <!-- 1. Business growth -->
    @if (tab() === 'business' && business(); as b) {
      <div class="stats four">
        <article class="stat purple"><div class="stat-top">MONTHLY RECURRING REVENUE</div><div class="stat-value">{{ b.currentMrr | money }}</div><small>{{ b.academies.paying }} paying academies</small></article>
        <article class="stat green"><div class="stat-top">EARNED THIS MONTH</div><div class="stat-value">{{ b.earnedThisMonth | money }}</div><small>Invoices spread over the months they pay for</small></article>
        <article class="stat yellow"><div class="stat-top">TRIAL CONVERSION</div><div class="stat-value">{{ b.trials.conversionPercent }}%</div><small>{{ b.trials.converted }} of {{ b.trials.decided }} paid · {{ b.trials.averageDaysToPay != null ? 'avg ' + b.trials.averageDaysToPay + ' days to pay' : 'no payments yet' }}</small></article>
        <article class="stat white"><div class="stat-top">ACADEMIES</div><div class="stat-value">{{ b.academies.total }}</div><small>{{ b.academies.trial }} trial · {{ b.academies.pastDue }} past due · {{ b.academies.suspended }} suspended · {{ b.academies.newThisMonth }} new this month</small></article>
      </div>

      <section class="surface">
        <div class="surface-title"><h2>Revenue by month</h2><div class="chart-legend"><span><i class="changed"></i>Monthly revenue</span><span><i class="created"></i>Collected</span></div></div>
        <div class="timeline pair">
          @for (m of b.months; track m.month) {
            <div class="timeline-col" [title]="m.label + ': revenue ₹' + m.mrr + ', collected ₹' + m.collected">
              <div class="timeline-bar duo">
                <span class="changed" [style.height.%]="pct(m.mrr, maxMoney())"></span>
                <span class="created" [style.height.%]="pct(m.collected, maxMoney())"></span>
              </div>
              <small>{{ m.label.slice(0, 3) }}</small>
            </div>
          }
        </div>
        <div class="table-scroll gap-top">
          <table>
            <thead><tr><th>MONTH</th><th>REVENUE</th><th>COLLECTED</th><th>NEW</th><th>UPGRADES</th><th>DOWNGRADES</th><th>LOST</th><th>NEW ACADEMIES</th><th>LEFT</th></tr></thead>
            <tbody>
              @for (m of b.months.slice().reverse(); track m.month) {
                <tr>
                  <td><strong>{{ m.label }}</strong></td>
                  <td>{{ m.mrr | money }}</td>
                  <td>{{ m.collected | money }}</td>
                  <td class="positive">{{ m.newMrr ? '+' : '' }}{{ m.newMrr | money }}</td>
                  <td class="positive">{{ m.expansion ? '+' : '' }}{{ m.expansion | money }}</td>
                  <td class="negative">{{ m.contraction ? '−' : '' }}{{ m.contraction | money }}</td>
                  <td class="negative">{{ m.churned ? '−' : '' }}{{ m.churned | money }}</td>
                  <td>{{ m.newAcademies }}</td>
                  <td>{{ m.lostAcademies }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      <div class="activity-grid">
        <section class="surface">
          <div class="surface-title"><h2>Revenue by plan</h2>@if (b.popularPlan) {<span>MOST POPULAR: {{ b.popularPlan.toUpperCase() }}</span>}</div>
          @for (p of b.byPlan; track p.plan) {
            <div class="hbar-row user">
              <span class="hbar-label"><strong>{{ p.plan }}</strong><small>{{ p.paying }} paying · {{ p.trial }} trial</small></span>
              <div class="hbar"><span class="changed" [style.width.%]="pct(p.mrr, b.byPlan[0].mrr || 1)"></span></div>
              <b>{{ p.mrr | money }}</b>
            </div>
          } @empty {
            <p class="helper">No subscriptions yet.</p>
          }
        </section>
        <section class="surface">
          <div class="surface-title"><h2>Upgrade opportunities</h2><span>80%+ OF PLAN LIMIT</span></div>
          @for (u of b.upgrades; track u.tenantId) {
            <div class="mini-row">
              <a [routerLink]="['/platform/academies', u.tenantId]"><strong>{{ u.academy }}</strong></a>
              <small>{{ u.plan }} · students {{ u.students }}/{{ u.maxStudents ?? '∞' }} · users {{ u.users }}/{{ u.maxUsers ?? '∞' }}</small>
              <span class="pill" [class.bad]="u.usePercent >= 100">{{ u.usePercent }}%</span>
            </div>
          } @empty {
            <p class="helper">No academy is near its plan's limits.</p>
          }
        </section>
      </div>

      <div class="activity-grid">
        <section class="surface">
          <div class="surface-title"><h2>Renewals in the next 30 days</h2></div>
          @for (r of b.renewals; track r.tenantId) {
            <div class="mini-row">
              <a [routerLink]="['/platform/academies', r.tenantId]"><strong>{{ r.academy }}</strong></a>
              <small>{{ r.plan }} · {{ r.price | money }} · {{ r.status }}</small>
              <span>{{ r.renewsOn | day }}</span>
            </div>
          } @empty {
            <p class="helper">No renewals due.</p>
          }
        </section>
        <section class="surface">
          <div class="surface-title"><h2>Overdue invoices</h2></div>
          @for (o of b.overdue; track o.invoiceNumber) {
            <div class="mini-row">
              <a [routerLink]="['/platform/academies', o.tenantId]"><strong>{{ o.academy }}</strong></a>
              <small>{{ o.invoiceNumber }} · due {{ o.dueOn | day }}</small>
              <span class="pill bad">{{ o.amount | money }} · {{ o.daysOverdue }}d</span>
            </div>
          } @empty {
            <p class="helper">Nothing overdue.</p>
          }
        </section>
      </div>

      <section class="surface">
        <div class="surface-title"><h2>Academies lost</h2><span>SUSPENDED OR CANCELLED, WITH THE REASON</span></div>
        @for (l of b.lost; track l.tenantId) {
          <div class="mini-row">
            <a [routerLink]="['/platform/academies', l.tenantId]"><strong>{{ l.academy }}</strong></a>
            <small>{{ l.reason || 'No reason recorded' }}</small>
            <span>{{ l.on | day }}</span>
          </div>
        } @empty {
          <p class="helper">No academy has left.</p>
        }
      </section>
    }

    <!-- 2. Academy health -->
    @if (tab() === 'health' && health(); as h) {
      <div class="stats four">
        <article class="stat green"><div class="stat-top">🟢 ACTIVE</div><div class="stat-value">{{ h.counts.active }}</div><small>Used regularly</small></article>
        <article class="stat yellow"><div class="stat-top">🟡 SLOWING DOWN</div><div class="stat-value">{{ h.counts.slowing }}</div><small>Idle 5+ days or low score</small></article>
        <article class="stat pink"><div class="stat-top">🔴 GOING QUIET</div><div class="stat-value">{{ h.counts.quiet }}</div><small>No activity for 10+ days — call them</small></article>
        <article class="stat white"><div class="stat-top">SUSPENDED</div><div class="stat-value">{{ h.counts.suspended }}</div><small>Locked out</small></article>
        <article class="stat white"><div class="stat-top">STUDENTS ON PLATFORM</div><div class="stat-value">{{ h.platformTotals.students }}</div><small>{{ h.platformTotals.activeStudents }} active</small></article>
        <article class="stat white"><div class="stat-top">FACULTY</div><div class="stat-value">{{ h.platformTotals.faculty }}</div><small>{{ h.platformTotals.portalUsers }} portal logins</small></article>
        <article class="stat white"><div class="stat-top">STAFF USERS</div><div class="stat-value">{{ h.platformTotals.staffUsers }}</div><small>Admins, accountants, front desk</small></article>
        <article class="stat purple"><div class="stat-top">FEES RECORDED</div><div class="stat-value">{{ h.platformTotals.feesTotal | money }}</div><small>{{ h.platformTotals.feesRecorded }} payments through the platform</small></article>
      </div>

      <section class="surface">
        <div class="surface-title"><h2>Onboarding — where new academies get stuck</h2></div>
        @for (s of h.onboardingFunnel; track s.step) {
          <div class="hbar-row">
            <span class="hbar-label">{{ s.step }}</span>
            <div class="hbar"><span class="created" [style.width.%]="s.percent"></span></div>
            <b>{{ s.percent }}%</b>
          </div>
        }
      </section>

      <section class="surface table-scroll">
        <table>
          <thead><tr><th>ACADEMY</th><th>HEALTH</th><th>SCORE</th><th>LAST ACTIVE</th><th>ACTIVE DAYS (14)</th><th>FEATURES (14)</th><th>SET-UP</th><th>STUDENTS</th><th>FEES (30 DAYS)</th><th>PLAN</th></tr></thead>
          <tbody>
            @for (a of h.academies; track a.tenantId) {
              <tr class="clickable" (click)="expanded.set(expanded() === a.tenantId ? '' : a.tenantId)">
                <td><a [routerLink]="['/platform/academies', a.tenantId]" (click)="$event.stopPropagation()"><strong>{{ a.academy }}</strong></a><small>{{ a.code }}</small></td>
                <td><span class="health-pill" [class]="'health-pill ' + a.health">{{ healthLabel(a.health) }}</span></td>
                <td><div class="score"><div class="hbar"><span [class]="scoreTone(a.score)" [style.width.%]="a.score"></span></div><b>{{ a.score }}</b></div></td>
                <td>{{ ago(a.lastActiveUtc) }}</td>
                <td>{{ a.activeDays14 }}</td>
                <td>{{ a.features14 }}</td>
                <td [title]="missing(a)">{{ a.onboardingDone }}/6</td>
                <td>{{ a.totals.activeStudents }}<small>of {{ a.totals.students }}</small></td>
                <td>{{ a.totals.fees30 | money }}</td>
                <td>{{ a.plan ?? '—' }}<small>{{ a.subscription }}</small></td>
              </tr>
              @if (expanded() === a.tenantId) {
                <tr class="detail-row">
                  <td colspan="10">
                    <div class="health-detail">
                      <div>
                        <b>Last sign-in by role</b>
                        @for (r of a.lastByRole; track r.role) {
                          <div>{{ roleLabel(r.role) }}: {{ ago(r.lastUtc) }}</div>
                        } @empty {
                          <div class="muted">Nobody has signed in yet.</div>
                        }
                      </div>
                      <div>
                        <b>Set-up checklist</b>
                        @for (s of a.onboarding; track s.step) {
                          <div [class.muted]="!s.done">{{ s.done ? '✓' : '○' }} {{ s.step }}</div>
                        }
                      </div>
                      <div>
                        <b>Totals</b>
                        <div>{{ a.totals.faculty }} faculty · {{ a.totals.staffUsers }} staff users · {{ a.totals.portalUsers }} portal logins</div>
                        <div>{{ a.totals.feesRecorded }} payments recorded, {{ a.totals.feesTotal | money }} in all</div>
                        <div>{{ a.totals.expenses }} expenses · joined {{ a.createdAtUtc | day }}</div>
                      </div>
                    </div>
                  </td>
                </tr>
              }
            }
          </tbody>
        </table>
      </section>
      <p class="helper">Score out of 100: days active in the last 14 (40), fees recorded (20), students added (10) and number of features used (30). Click a row for details.</p>
    }

    <!-- 3. Feature usage -->
    @if (tab() === 'usage' && usage(); as u) {
      @if (!u.trackingSince) {
        <div class="info-strip">Usage tracking starts with this release. Figures appear as academies use the system.</div>
      } @else {
        <div class="info-strip">Tracking since {{ u.trackingSince | day }}.</div>
      }
      <div class="stats four">
        <article class="stat purple"><div class="stat-top">REQUESTS</div><div class="stat-value">{{ u.totals.requests }}</div><small>Screens loaded and actions taken</small></article>
        <article class="stat white"><div class="stat-top">ACADEMIES USING IT</div><div class="stat-value">{{ u.totals.academiesUsing }}</div><small>of {{ u.totals.activeAcademies }} active</small></article>
        <article class="stat white"><div class="stat-top">ACTIVE USERS</div><div class="stat-value">{{ u.totals.activeUsers }}</div><small>Different logins seen</small></article>
        <article class="stat yellow"><div class="stat-top">ON A PHONE</div><div class="stat-value">{{ u.totals.mobilePercent }}%</div><small>{{ u.devices.mobile }} mobile · {{ u.devices.desktop }} desktop</small></article>
      </div>

      <div class="activity-grid">
        <section class="surface">
          <div class="surface-title"><h2>Portal adoption</h2><span>LOGINS GIVEN VS USED</span></div>
          <div class="hbar-row user">
            <span class="hbar-label"><strong>Faculty portal</strong><small>{{ u.portals.faculty.used }} of {{ u.portals.faculty.given }} used it</small></span>
            <div class="hbar"><span class="created" [style.width.%]="pct(u.portals.faculty.used, u.portals.faculty.given || 1)"></span></div>
            <b>{{ round(pct(u.portals.faculty.used, u.portals.faculty.given || 1)) }}%</b>
          </div>
          <div class="hbar-row user">
            <span class="hbar-label"><strong>Student portal</strong><small>{{ u.portals.students.used }} of {{ u.portals.students.given }} used it</small></span>
            <div class="hbar"><span class="created" [style.width.%]="pct(u.portals.students.used, u.portals.students.given || 1)"></span></div>
            <b>{{ round(pct(u.portals.students.used, u.portals.students.given || 1)) }}%</b>
          </div>
        </section>
        <section class="surface">
          <div class="surface-title"><h2>Not used in this period</h2></div>
          <div class="chips">
            @for (f of u.unused; track f) {
              <span class="chip">{{ f }}</span>
            } @empty {
              <p class="helper">Every feature was used.</p>
            }
          </div>
        </section>
      </div>

      <section class="surface table-scroll">
        <div class="surface-title"><h2>Features, most used first</h2><span>LAST 8 WEEKS →</span></div>
        <table>
          <thead><tr><th>FEATURE</th><th>USE</th><th>ACADEMIES</th><th>AVG SPEED</th><th>PHONE</th><th>MOSTLY BY</th><th>8 WEEKS</th></tr></thead>
          <tbody>
            @for (f of u.features; track f.feature) {
              <tr [class.dimmed]="!f.requests">
                <td><strong>{{ f.feature }}</strong></td>
                <td><div class="score wide"><div class="hbar"><span class="changed" [style.width.%]="pct(f.requests, u.features[0].requests || 1)"></span></div><b>{{ f.requests }}</b></div></td>
                <td>{{ f.academies }}<small>{{ f.adoptionPercent }}% adoption</small></td>
                <td>{{ f.requests ? f.avgMs + ' ms' : '—' }}</td>
                <td>{{ f.requests ? f.mobilePercent + '%' : '—' }}</td>
                <td>{{ topRole(f.byRole) }}</td>
                <td><div class="spark" [title]="sparkTitle(u, f.feature)">@for (n of u.weekly[f.feature] ?? zeros; track $index) {<i [style.height.%]="pct(n, sparkMax(u.weekly[f.feature]))"></i>}</div></td>
              </tr>
            }
          </tbody>
        </table>
      </section>

      <section class="surface">
        <div class="surface-title"><h2>What each role does</h2></div>
        <div class="role-grid">
          @for (r of u.roles; track r.role) {
            <article>
              <strong>{{ roleLabel(r.role) }}</strong>
              <small>{{ r.requests }} requests · {{ r.mobilePercent }}% on a phone</small>
              @for (f of r.topFeatures; track f.feature) {
                <div class="hbar-row"><span class="hbar-label">{{ f.feature }}</span><div class="hbar"><span class="changed" [style.width.%]="pct(f.requests, r.topFeatures[0].requests)"></span></div><b>{{ f.requests }}</b></div>
              }
            </article>
          }
        </div>
      </section>
    }

    <!-- 4. Peak hours and speed -->
    @if (tab() === 'load' && loadData(); as l) {
      <div class="stats four">
        <article class="stat purple"><div class="stat-top">PEAK HOUR</div><div class="stat-value">{{ l.peakHour != null ? hourRange(l.peakHour, 1) : '—' }}</div><small>Busiest hour of the day (Indian time)</small></article>
        <article class="stat green"><div class="stat-top">QUIET WINDOW</div><div class="stat-value small-value">{{ l.quietStart != null ? hourRange(l.quietStart, 3) : '—' }}</div><small>Best time to deploy and run maintenance</small></article>
        <article class="stat white"><div class="stat-top">BUSIEST MINUTE</div><div class="stat-value">{{ l.peakPerMinute }}</div><small>{{ l.peakMinuteUtc ? 'requests/min · ' + when(l.peakMinuteUtc) : 'requests per minute (all academies)' }}</small></article>
        <article class="stat white"><div class="stat-top">MOST PEOPLE ONLINE</div><div class="stat-value">{{ l.mostOnline }}</div><small>{{ l.mostOnlineHourUtc ? 'in one hour · ' + when(l.mostOnlineHourUtc) : 'in one hour' }}</small></article>
        <article class="stat white"><div class="stat-top">TYPICAL RESPONSE</div><div class="stat-value">{{ l.totals.avgMs }} ms</div><small>Half answered within {{ l.totals.p50Ms }} ms</small></article>
        <article class="stat" [class.amber]="l.totals.p95Ms >= 1000" [class.white]="l.totals.p95Ms < 1000"><div class="stat-top">SLOWEST 5%</div><div class="stat-value">{{ l.totals.p95Ms >= 3000 ? 'over 3 s' : l.totals.p95Ms + ' ms' }}</div><small>95% answered within this · slowest {{ l.totals.maxMs }} ms</small></article>
        <article class="stat" [class.pink]="l.totals.serverErrors > 0" [class.white]="!l.totals.serverErrors"><div class="stat-top">SERVER ERRORS</div><div class="stat-value">{{ l.totals.serverErrors }}</div><small>{{ l.totals.errorPercent }}% of requests · {{ l.totals.clientErrors }} refused (invalid or not allowed)</small></article>
        <article class="stat white"><div class="stat-top">REQUESTS</div><div class="stat-value">{{ l.totals.requests }}</div><small>{{ l.totals.slowRequests }} took over 1 second</small></article>
      </div>

      <section class="surface advice">
        <div class="surface-title"><h2>Deployment advice</h2></div>
        <ul>
          @for (a of advice(); track a) {
            <li>{{ a }}</li>
          }
        </ul>
      </section>

      <div class="activity-grid">
        <section class="surface">
          <div class="surface-title"><h2>Busiest hours{{ l.academy ? ' — ' + l.academy : '' }}</h2><span>INDIAN TIME</span></div>
          <div class="heatmap">
            <div class="heat-row heat-hours"><span></span>@for (h of hours; track h) {<small>{{ h % 3 === 0 ? hourLabel(h) : '' }}</small>}</div>
            @for (row of l.heatmap; track $index; let day = $index) {
              <div class="heat-row">
                <span class="heat-day">{{ days[day] }}</span>
                @for (count of row; track $index; let h = $index) {
                  <i class="heat-cell" [class.night]="h >= 22 || h < 6" [style.background]="heat(count, l)" [title]="days[day] + ' ' + hourLabel(h) + ': ' + count + ' requests'"></i>
                }
              </div>
            }
          </div>
        </section>
        <section class="surface">
          <div class="surface-title"><h2>By hour of the day</h2></div>
          <div class="timeline hours">
            @for (n of l.byHour; track $index; let h = $index) {
              <div class="timeline-col" [title]="hourLabel(h) + ': ' + n + ' requests'">
                <div class="timeline-bar"><span class="changed" [class.peak]="h === l.peakHour" [style.height.%]="pct(n, maxOf(l.byHour))"></span></div>
                <small>{{ h % 6 === 0 ? hourLabel(h) : '' }}</small>
              </div>
            }
          </div>
        </section>
      </div>

      <section class="surface">
        <div class="surface-title"><h2>Day by day</h2><div class="chart-legend"><span><i class="changed"></i>Requests</span><span><i class="removed"></i>Server errors</span></div></div>
        <div class="timeline dense">
          @for (d of l.daily; track d.date; let i = $index) {
            <div class="timeline-col" [title]="d.date + ': ' + d.requests + ' requests, avg ' + d.avgMs + ' ms, ' + d.serverErrors + ' errors'">
              <div class="timeline-bar">
                <span class="removed" [style.height.%]="pct(d.serverErrors, maxDaily(l))"></span>
                <span class="changed" [style.height.%]="pct(d.requests - d.serverErrors, maxDaily(l))"></span>
              </div>
              <small>{{ i % 5 === 0 || i === l.daily.length - 1 ? d.date.slice(8, 10) + '/' + d.date.slice(5, 7) : '' }}</small>
            </div>
          }
        </div>
      </section>

      <div class="activity-grid">
        <section class="surface table-scroll">
          <div class="surface-title"><h2>Slowest features</h2></div>
          <table>
            <thead><tr><th>FEATURE</th><th>AVG</th><th>95% WITHIN</th><th>SLOWEST</th><th>USE</th></tr></thead>
            <tbody>
              @for (s of l.slowest; track s.feature) {
                <tr><td><strong>{{ s.feature }}</strong></td><td>{{ s.avgMs }} ms</td><td [class.negative]="s.p95Ms >= 1000">{{ s.p95Ms >= 3000 ? '> 3 s' : s.p95Ms + ' ms' }}</td><td>{{ s.maxMs }} ms</td><td>{{ s.requests }}</td></tr>
              } @empty {
                <tr><td colspan="5" class="empty">Not enough use yet.</td></tr>
              }
            </tbody>
          </table>
        </section>
        <section class="surface table-scroll">
          <div class="surface-title"><h2>Errors by feature</h2></div>
          <table>
            <thead><tr><th>FEATURE</th><th>SERVER ERRORS</th><th>REFUSED</th></tr></thead>
            <tbody>
              @for (e of l.errors; track e.feature) {
                <tr><td><strong>{{ e.feature }}</strong></td><td [class.negative]="e.serverErrors > 0">{{ e.serverErrors }}</td><td>{{ e.clientErrors }}</td></tr>
              } @empty {
                <tr><td colspan="3" class="empty">No errors.</td></tr>
              }
            </tbody>
          </table>
          <p class="helper">"Refused" are requests turned away as invalid or not allowed (wrong password, missing field) — normal in small numbers.</p>
        </section>
      </div>

      @if (!l.tenantId) {
        <section class="surface table-scroll">
          <div class="surface-title"><h2>Usage by academy</h2></div>
          <table>
            <thead><tr><th>ACADEMY</th><th>SHARE OF LOAD</th><th>REQUESTS</th><th>THEIR PEAK HOUR</th><th>LATE NIGHT (10 PM – 6 AM)</th></tr></thead>
            <tbody>
              @for (a of l.perAcademy; track a.tenantId) {
                <tr class="clickable" (click)="academyId.set(a.tenantId); load()">
                  <td><strong>{{ a.academy }}</strong></td>
                  <td><div class="score wide"><div class="hbar"><span class="changed" [style.width.%]="a.sharePercent"></span></div><b>{{ a.sharePercent }}%</b></div></td>
                  <td>{{ a.requests }}</td>
                  <td>{{ hourRange(a.peakHour, 1) }}</td>
                  <td>{{ a.lateNightPercent }}%</td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="empty">No usage recorded yet.</td></tr>
              }
            </tbody>
          </table>
          <p class="helper">Click an academy to see its own heatmap.</p>
        </section>
      }

      <section class="surface">
        <div class="surface-title"><h2>Background work</h2></div>
        <div class="stats four compact">
          <article class="stat white"><div class="stat-top">EMAILS SENT</div><div class="stat-value">{{ l.jobs.emailsSent }}</div><small>{{ l.jobs.emailsFailed }} failed · {{ l.jobs.emailsWaiting }} waiting</small></article>
          <article class="stat white"><div class="stat-top">INVOICES RAISED</div><div class="stat-value">{{ l.jobs.invoicesRaised }}</div><small>By the billing run and by hand</small></article>
          <article class="stat white"><div class="stat-top">CLASSES AUTO-CLOSED</div><div class="stat-value">{{ l.jobs.classesAutoClosed }}</div><small>Left running at midnight</small></article>
          <article class="stat white"><div class="stat-top">CLASS REMINDERS</div><div class="stat-value">{{ l.jobs.classRemindersSent }}</div><small>"Still teaching?" emails</small></article>
        </div>
        <div class="chips">
          @for (e of l.jobs.emailsByType; track e.category) {
            <span class="chip">{{ e.category }}: {{ e.count }}</span>
          }
        </div>
      </section>
    }

    <!-- 5. Capacity and cost -->
    @if (tab() === 'capacity' && capacity(); as c) {
      <div class="page-actions-row">
        <button type="button" class="secondary" (click)="openSettings(c)">⚙ Hosting cost and limits</button>
      </div>
      <div class="stats four">
        <article class="stat" [class.pink]="c.database.usedPercent >= 80" [class.purple]="c.database.usedPercent < 80">
          <div class="stat-top">DATABASE</div><div class="stat-value">{{ bytes(c.database.bytes) }}</div>
          <div class="hbar gap-top"><span [class]="c.database.usedPercent >= 80 ? 'removed' : 'changed'" [style.width.%]="min(c.database.usedPercent, 100)"></span></div>
          <small>{{ c.database.usedPercent }}% of {{ bytes(c.database.limitBytes) }}</small>
        </article>
        <article class="stat white"><div class="stat-top">GROWTH PER MONTH</div><div class="stat-value">{{ bytes(c.database.monthlyGrowthBytes) }}</div><small>{{ c.database.growthMeasured ? 'Measured from daily sizes' : 'Estimated from the last 30 days of new records' }}</small></article>
        <article class="stat" [class.amber]="c.database.monthsLeft != null && c.database.monthsLeft < 6" [class.white]="c.database.monthsLeft == null || c.database.monthsLeft >= 6"><div class="stat-top">FULL IN</div><div class="stat-value">{{ duration(c.database.monthsLeft) }}</div><small>At the current growth</small></article>
        <article class="stat white"><div class="stat-top">BILL FILES</div><div class="stat-value">{{ bytes(c.database.filesBytes) }}</div><small>Expense proofs stored in the database</small></article>
        <article class="stat" [class.pink]="c.email.todayPercent >= 80" [class.white]="c.email.todayPercent < 80"><div class="stat-top">EMAILS TODAY</div><div class="stat-value">{{ c.email.today }} / {{ c.email.dailyLimit }}</div><small>{{ c.email.todayPercent }}% of the daily limit · busiest day {{ c.email.busiestDayCount }}</small></article>
        <article class="stat white"><div class="stat-top">EMAILS (30 DAYS)</div><div class="stat-value">{{ c.email.last30 }}</div><small>{{ c.email.failed30 }} failed</small></article>
        <article class="stat yellow"><div class="stat-top">HOSTING COST</div><div class="stat-value">{{ c.hosting.monthlyCost | money }}</div><small>{{ c.hosting.monthlyCost ? (c.hosting.costPerAcademy | money) + ' per active academy' : 'Set it to see cost per academy' }}</small></article>
        <article class="stat green"><div class="stat-top">REVENUE VS COST</div><div class="stat-value">{{ c.hosting.monthlyRevenue - c.hosting.monthlyCost | money }}</div><small>{{ c.hosting.monthlyRevenue | money }} revenue a month</small></article>
      </div>

      @if (c.database.history.length > 1) {
        <section class="surface">
          <div class="surface-title"><h2>Database size, day by day</h2></div>
          <div class="timeline dense">
            @for (d of c.database.history; track d.date; let i = $index) {
              <div class="timeline-col" [title]="d.date + ': ' + bytes(d.bytes)">
                <div class="timeline-bar"><span class="changed" [style.height.%]="pct(d.bytes, c.database.limitBytes)"></span></div>
                <small>{{ i % 10 === 0 ? d.date.slice(8, 10) + '/' + d.date.slice(5, 7) : '' }}</small>
              </div>
            }
          </div>
        </section>
      }

      <section class="surface table-scroll">
        <div class="surface-title"><h2>Each academy: storage, email, load, cost and revenue</h2><span>COST SHARE = HALF BY LOAD, HALF BY STORAGE</span></div>
        <table>
          <thead><tr><th>ACADEMY</th><th>STORAGE</th><th>BILL FILES</th><th>EMAILS (30D)</th><th>REQUESTS (30D)</th><th>COST SHARE</th><th>COST</th><th>REVENUE</th><th>MARGIN</th></tr></thead>
          <tbody>
            @for (a of c.academies; track a.tenantId) {
              <tr>
                <td><a [routerLink]="['/platform/academies', a.tenantId]"><strong>{{ a.academy }}</strong></a><small>{{ a.status }}</small></td>
                <td>{{ bytes(a.storageBytes) }}</td>
                <td>{{ bytes(a.filesBytes) }}</td>
                <td>{{ a.emails30 }}@if (a.emailsFailed30) {<small class="negative">{{ a.emailsFailed30 }} failed</small>}</td>
                <td>{{ a.requests30 }}</td>
                <td>{{ a.costSharePercent }}%</td>
                <td>{{ a.cost | money }}</td>
                <td>{{ a.revenue | money }}</td>
                <td [class.negative]="a.margin < 0" [class.positive]="a.margin > 0">{{ a.margin | money }}</td>
              </tr>
            }
          </tbody>
        </table>
      </section>

      <section class="surface table-scroll">
        <div class="surface-title"><h2>Biggest tables</h2></div>
        <table>
          <thead><tr><th>TABLE</th><th>SIZE</th><th>ROWS</th></tr></thead>
          <tbody>
            @for (t of c.database.tables; track t.table) {
              <tr><td>{{ t.table }}</td><td>{{ bytes(t.bytes) }}</td><td>{{ t.rows }}</td></tr>
            }
          </tbody>
        </table>
      </section>
    }

    @if (settingsForm.open()) {
      <app-modal title="Hosting cost and limits" [busy]="settingsForm.busy()" [error]="settingsForm.error()" (closed)="settingsForm.close()" (saved)="saveSettings()">
        <label for="cost">Monthly hosting cost (₹)</label>
        <input id="cost" type="number" min="0" step="1" [(ngModel)]="settings.monthlyHostingCost" />
        <div class="helper">Everything you pay to run the platform each month: Render server and database, email service, domain. Used to work out each academy's cost and margin.</div>
        <label for="db">Database limit (MB)</label>
        <input id="db" type="number" min="100" step="1" [(ngModel)]="settings.databaseLimitMb" />
        <div class="helper">The storage of your database plan, for the "full in" estimate.</div>
        <label for="mail">Daily email limit</label>
        <input id="mail" type="number" min="1" step="1" [(ngModel)]="settings.emailDailyLimit" />
        <div class="helper">Resend's free plan allows 100 emails a day.</div>
      </app-modal>
    }
  `,
})
export class PlatformAnalyticsComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  protected readonly tabs: { id: Tab; label: string }[] = [
    { id: 'business', label: 'Business growth' },
    { id: 'health', label: 'Academy health' },
    { id: 'usage', label: 'Feature usage' },
    { id: 'load', label: 'Peak hours & speed' },
    { id: 'capacity', label: 'Capacity & cost' },
  ];
  protected readonly ranges = [
    { label: 'Today', days: 1 },
    { label: '7 days', days: 7 },
    { label: '30 days', days: 30 },
    { label: '90 days', days: 90 },
  ];
  protected readonly days = DAYS;
  protected readonly hours = Array.from({ length: 24 }, (_, i) => i);
  protected readonly zeros = [0, 0, 0, 0, 0, 0, 0, 0];

  protected readonly tab = signal<Tab>('business');
  protected readonly rangeDays = signal(30);
  protected readonly academyId = signal('');
  protected readonly error = signal('');
  protected readonly expanded = signal('');
  protected readonly business = signal<any>(null);
  protected readonly health = signal<any>(null);
  protected readonly usage = signal<any>(null);
  protected readonly loadData = signal<any>(null);
  protected readonly capacity = signal<any>(null);
  protected readonly settingsForm = new FormState();
  protected settings = { monthlyHostingCost: 0, databaseLimitMb: 1024, emailDailyLimit: 100 };
  private readonly academies = signal<{ tenantId: string; academy: string }[]>([]);

  protected readonly academyOptions = computed(() => this.academies());
  /** The data of the open tab, null while it loads. */
  protected readonly current = computed(() =>
    ({ business: this.business, health: this.health, usage: this.usage, load: this.loadData, capacity: this.capacity })[this.tab()](),
  );
  protected readonly maxMoney = computed(() =>
    Math.max(1, ...((this.business()?.months ?? []) as any[]).flatMap(m => [m.mrr, m.collected])),
  );

  /** Plain-language advice from the peak-hour and speed numbers. */
  protected readonly advice = computed(() => {
    const l = this.loadData();
    if (!l || !l.totals.requests) return ['Not enough usage recorded yet. Advice appears after a few days of use.'];
    const out: string[] = [];
    if (l.quietStart != null) out.push(`Deploy and run maintenance between ${this.hourRange(l.quietStart, 3)} (Indian time), the quietest three hours.`);
    if (l.peakHour != null) out.push(`Avoid deploying around ${this.hourRange(l.peakHour, 1)}, the busiest hour.`);
    if (l.totals.p95Ms >= 1000) out.push('The slowest 5% of requests take over a second: consider a larger server or look at the slowest features below.');
    else out.push('Response times are healthy: 95% of requests finish within ' + l.totals.p95Ms + ' ms. The current server size is enough.');
    if (l.totals.errorPercent >= 1) out.push(`Server errors are ${l.totals.errorPercent}% of requests — check the errors table and the server logs.`);
    if (l.peakPerMinute >= 300) out.push('Peak load is over 300 requests a minute: plan a bigger instance or a second one before it grows further.');
    return out;
  });

  constructor() {
    this.open('business');
  }

  protected open(tab: Tab): void {
    this.tab.set(tab);
    this.load();
  }

  protected pickRange(days: number): void {
    this.rangeDays.set(days);
    this.load();
  }

  protected load(): void {
    this.error.set('');
    const to = today();
    const from = shiftDays(to, -(this.rangeDays() - 1));
    const fail = (e: unknown) => this.error.set(errorMessage(e));
    switch (this.tab()) {
      case 'business':
        this.api.get<any>('platform/analytics/business', { months: 12 }).subscribe({ next: d => this.business.set(d), error: fail });
        break;
      case 'health':
        this.api.get<any>('platform/analytics/health').subscribe({ next: d => this.health.set(d), error: fail });
        break;
      case 'usage':
        this.api.get<any>('platform/analytics/usage', { from, to }).subscribe({ next: d => this.usage.set(d), error: fail });
        break;
      case 'load':
        this.api.get<any>('platform/analytics/load', { from, to, tenantId: this.academyId() || null }).subscribe({
          next: d => {
            this.loadData.set(d);
            if (!d.tenantId) this.academies.set(d.perAcademy);
          },
          error: fail,
        });
        if (!this.academies().length && this.academyId()) this.academyId.set('');
        break;
      case 'capacity':
        this.api.get<any>('platform/analytics/capacity').subscribe({ next: d => this.capacity.set(d), error: fail });
        break;
    }
  }

  protected openSettings(c: any): void {
    this.settings = { ...c.settings };
    this.settingsForm.show();
  }

  protected saveSettings(): void {
    this.settingsForm.submit(this.api.put('platform/analytics/settings', this.settings), () => {
      this.notify.success('Settings saved');
      this.load();
    });
  }

  protected pct(value: number, max: number): number {
    return max > 0 ? Math.min(100, (value / max) * 100) : 0;
  }

  protected duration(months: number | null): string {
    if (months == null) return '—';
    if (months > 24) return `${Math.floor(months / 12)} years`;
    return `${months} months`;
  }

  protected round(n: number): number {
    return Math.round(n);
  }

  protected min(a: number, b: number): number {
    return Math.min(a, b);
  }

  protected maxOf(values: number[]): number {
    return Math.max(1, ...values);
  }

  protected maxDaily(l: any): number {
    return Math.max(1, ...l.daily.map((d: any) => d.requests));
  }

  protected sparkMax(values: number[] | undefined): number {
    return Math.max(1, ...(values ?? [0]));
  }

  protected sparkTitle(u: any, feature: string): string {
    const values: number[] = u.weekly[feature] ?? this.zeros;
    return values.map((n, i) => `${u.weeks[i]}: ${n}`).join('\n');
  }

  protected heat(count: number, l: any): string {
    if (!count) return '#f3f4f9';
    const max = Math.max(1, ...l.heatmap.flat());
    return `rgba(98, 85, 237, ${(0.15 + 0.85 * (count / max)).toFixed(2)})`;
  }

  protected hourLabel(h: number): string {
    return h === 0 ? '12a' : h < 12 ? `${h}a` : h === 12 ? '12p' : `${h - 12}p`;
  }

  protected hourRange(start: number, length: number): string {
    const label = (h: number) => {
      const hour = h % 24;
      return hour === 0 ? '12 AM' : hour < 12 ? `${hour} AM` : hour === 12 ? '12 PM' : `${hour - 12} PM`;
    };
    return `${label(start)} – ${label(start + length)}`;
  }

  protected when(utc: string): string {
    return new Date(utc).toLocaleString('en-IN', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', timeZone: 'Asia/Kolkata' });
  }

  protected ago(utc: string | null): string {
    if (!utc) return 'Never';
    const minutes = Math.floor((Date.now() - new Date(utc).getTime()) / 60000);
    if (minutes < 60) return minutes <= 1 ? 'Just now' : `${minutes} min ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours} h ago`;
    const days = Math.floor(hours / 24);
    return days === 1 ? 'Yesterday' : `${days} days ago`;
  }

  protected bytes(n: number): string {
    if (!n) return '0 KB';
    if (n >= 1024 ** 3) return `${(n / 1024 ** 3).toFixed(2)} GB`;
    if (n >= 1024 ** 2) return `${(n / 1024 ** 2).toFixed(1)} MB`;
    return `${Math.max(1, Math.round(n / 1024))} KB`;
  }

  protected healthLabel(h: string): string {
    return { active: '🟢 Active', slowing: '🟡 Slowing', quiet: '🔴 Going quiet', suspended: '⏸ Suspended' }[h] ?? h;
  }

  protected scoreTone(score: number): string {
    return score >= 60 ? 'created' : score >= 40 ? 'warn' : 'removed';
  }

  protected missing(a: any): string {
    const todo = (a.onboarding as any[]).filter(s => !s.done).map(s => s.step);
    return todo.length ? 'Still to do: ' + todo.join(', ') : 'Fully set up';
  }

  protected roleLabel(role: string): string {
    return role === 'SuperAdmin' ? 'Super Admin' : role === 'Visitor' ? 'Not signed in' : role;
  }

  protected topRole(byRole: Record<string, number>): string {
    const top = Object.entries(byRole).sort((a, b) => b[1] - a[1])[0];
    return top ? this.roleLabel(top[0]) : '—';
  }
}
