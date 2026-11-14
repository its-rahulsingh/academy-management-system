import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { Dashboard, Permission } from '../../core/models';
import { UI, initials } from '../../shared/ui';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE <span>•</span> LIVE</div>
        <h1>Dashboard</h1>
        <p>{{ finance() ? 'Fees, payouts and cash position in one place.' : 'Your academy at a glance.' }}</p>
      </div>
      <div class="page-actions">
        @if (auth.can(permission.StudentsWrite)) {
          <a class="primary" routerLink="/app/students" [queryParams]="{ new: 1 }">+ Add student</a>
        }
      </div>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (data(); as d) {
      @if (finance(); as f) {
        <div class="stats four">
          <article class="stat purple"><div class="stat-top">COLLECTED</div><div class="stat-value">{{ f.totalRevenue | money }}</div><small>Fees received, net of refunds</small></article>
          <article class="stat yellow"><div class="stat-top">PENDING FEES</div><div class="stat-value">{{ f.pendingFees | money }}</div><small>Outstanding student balances</small></article>
          <article class="stat pink"><div class="stat-top">FACULTY PAYABLE</div><div class="stat-value">{{ f.facultyPayable | money }}</div><small>Agreed payouts not yet paid</small></article>
          <article class="stat green"><div class="stat-top">NET CASH BALANCE</div><div class="stat-value">{{ f.netCashBalance | money }}</div><small>Collections minus expenses and payouts</small></article>
          <article class="stat rose"><div class="stat-top">EXPENSES</div><div class="stat-value">{{ f.totalExpenses | money }}</div><small>Operating spend recorded</small></article>
          <article class="stat amber"><div class="stat-top">FACULTY PAID</div><div class="stat-value">{{ f.facultyPaidOut | money }}</div><small>Payouts completed</small></article>
          <article class="stat white"><div class="stat-top">STUDENTS</div><div class="stat-value">{{ d.counts.totalStudents }}</div><small>{{ d.counts.activeStudents }} active</small></article>
          <article class="stat mint"><div class="stat-top">COURSES</div><div class="stat-value">{{ d.counts.totalCourses }}</div><small>{{ d.counts.totalFaculty }} faculty</small></article>
        </div>

        <div class="dashboard-grid">
          <section class="surface">
            <div class="surface-title"><h2>Collected by course</h2><span>ALL TIME</span></div>
            <p class="muted">Fees received for each course, net of refunds</p>
            @if (f.coursePerformance.length) {
              <div class="graph">
                @for (course of f.coursePerformance; track course.courseName) {
                  <div class="bar-group">
                    <div class="bar-area"><div class="bar" [style.height.%]="barHeight(course.collected)"></div></div>
                    <strong>{{ course.collected | money }}</strong>
                    <small>{{ course.courseName }}</small>
                  </div>
                }
              </div>
            } @else {
              <div class="empty">No courses yet. Add a course, enrol students and record fees.</div>
            }
          </section>
          <section class="surface">
            <div class="surface-title"><h2>Needs attention</h2><span>LIVE</span></div>
            <div class="insight yellow-note">{{ f.pendingFees | money }} in fees still to collect</div>
            <div class="insight pink-note">{{ f.facultyPayable | money }} owed to faculty</div>
            <div class="insight blue-note">{{ d.counts.activeStudents }} active students across {{ d.counts.totalCourses }} courses</div>
          </section>
        </div>

        <div class="dashboard-grid">
          <section class="surface">
            <div class="surface-title"><h2>Recent payments</h2><a class="text-button" routerLink="/app/payments">View all →</a></div>
            @for (payment of f.recentPayments; track payment.id) {
              <div class="recent-line">
                <div class="mini-avatar">{{ initialsOf(payment.studentName) }}</div>
                <div><strong>{{ payment.studentName }}</strong><small>{{ payment.paidOn | day }} • {{ payment.method }}</small></div>
                <b [class.positive]="payment.kind === 'Payment'" [class.negative]="payment.kind === 'Refund'">
                  {{ payment.kind === 'Refund' ? '−' : '+' }}{{ payment.amount | money }}
                </b>
              </div>
            } @empty {
              <p class="empty">No payments recorded yet.</p>
            }
          </section>
          <section class="surface">
            <div class="surface-title"><h2>Outstanding by course</h2><a class="text-button" routerLink="/app/courses">Courses →</a></div>
            @for (course of f.coursePerformance; track course.courseName) {
              <div class="performance-line">
                <div><strong>{{ course.courseName }}</strong><small>{{ course.students }} students</small></div>
                <b class="negative">{{ course.pending | money }}</b>
              </div>
            } @empty {
              <p class="empty">No courses yet.</p>
            }
          </section>
        </div>
      } @else {
        <div class="stats four">
          <article class="stat white"><div class="stat-top">STUDENTS</div><div class="stat-value">{{ d.counts.totalStudents }}</div><small>{{ d.counts.activeStudents }} active</small></article>
          <article class="stat mint"><div class="stat-top">COURSES</div><div class="stat-value">{{ d.counts.totalCourses }}</div></article>
        </div>
        <section class="surface">
          <div class="surface-title"><h2>Quick actions</h2></div>
          <div class="action-grid">
            <a routerLink="/app/students">Students</a>
            <a routerLink="/app/payments">Record a payment</a>
            <a routerLink="/app/tests">Tests and results</a>
          </div>
        </section>
      }
    } @else if (!error()) {
      <div class="empty">Loading…</div>
    }
  `,
})
export class DashboardComponent {
  protected readonly auth = inject(AuthService);
  protected readonly permission = Permission;
  protected readonly data = signal<Dashboard | null>(null);
  protected readonly error = signal('');
  protected readonly finance = computed(() => this.data()?.finance ?? null);
  private readonly highest = computed(() => Math.max(1, ...(this.finance()?.coursePerformance.map(c => c.collected) ?? [])));

  constructor() {
    inject(Api)
      .get<Dashboard>('dashboard')
      .subscribe({ next: data => this.data.set(data), error: error => this.error.set(errorMessage(error)) });
  }

  protected barHeight(amount: number): number {
    return Math.max(0, Math.min(100, (100 * amount) / this.highest()));
  }

  protected initialsOf(name: string): string {
    return initials(name);
  }
}
