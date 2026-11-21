import { Component, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, errorMessage } from '../../core/api';
import { StudentPortalOverview } from '../../core/models';
import { UI } from '../../shared/ui';
import { portalRefresh } from '../teacher/teacher-shell';

/** A student's own page: their course, fees and receipts, subjects and teachers, and latest test results. */
@Component({
  selector: 'app-student-dashboard',
  imports: [UI, RouterLink],
  template: `
    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (data(); as d) {
      <section class="teacher-hero student">
        <div>
          <div class="kicker">STUDENT DASHBOARD</div>
          <h1>Hello, {{ d.student.fullName }}</h1>
          <p>{{ d.student.courseName }} · {{ d.student.academicYearName }} · admitted {{ d.student.admissionDate | day }}</p>
        </div>
        <div class="hero-art" aria-hidden="true"><span>☆</span></div>
      </section>

      <div class="teacher-stats">
        <article class="tstat dark">
          <div class="tstat-top">TOTAL FEE<span>₹</span></div>
          <div class="tstat-value">{{ d.fees.payable | money }}</div>
          <small>
            @if (d.fees.discounts || d.fees.surcharges) {
              {{ d.fees.feeAmount | money }} − {{ d.fees.discounts | money }} discount{{ d.fees.surcharges ? ' + ' : '' }}{{ d.fees.surcharges ? (d.fees.surcharges | money) : '' }}
            } @else {
              Course fee
            }
          </small>
        </article>
        <article class="tstat sage">
          <div class="tstat-top">PAID<span>✓</span></div>
          <div class="tstat-value">{{ d.fees.paid | money }}</div>
          <small>{{ d.payments.length }} {{ d.payments.length === 1 ? 'receipt' : 'receipts' }}</small>
        </article>
        <article class="tstat rose">
          <div class="tstat-top">BALANCE DUE<span>!</span></div>
          <div class="tstat-value">{{ d.fees.pending | money }}</div>
          <small>{{ d.fees.pending > 0 ? 'Please pay at the academy' : 'Nothing due' }}</small>
        </article>
        <article class="tstat sky">
          <div class="tstat-top">FEE STATUS<span>▣</span></div>
          <div class="tstat-value small">{{ d.fees.status === 'Pending' ? 'Not paid' : d.fees.status === 'Partial' ? 'Part paid' : 'Fully paid' }}</div>
          <small>As of today</small>
        </article>
      </div>

      @if (d.subjects.length) {
        <h2 class="teacher-h2">My subjects</h2>
        <div class="teacher-subjects">
          @for (s of d.subjects; track s.name) {
            <article class="subject-card">
              <div class="subject-head">
                <span class="mini-avatar">{{ s.name.slice(0, 2).toUpperCase() }}</span>
                <div><strong>{{ s.name }}</strong><small>{{ s.faculty.length ? s.faculty.join(', ') : 'Teacher to be announced' }}</small></div>
              </div>
            </article>
          }
        </div>
      }

      <div class="student-grid">
        <section class="teacher-panel">
          <div class="panel-head"><h2 class="teacher-h2">Payments &amp; receipts</h2></div>
          @for (p of d.payments; track p.id) {
            <article class="session-row">
              <div class="session-date"><b>{{ p.paidOn.slice(8, 10) }}</b><small>{{ shortMonth(p.paidOn) }}</small></div>
              <div class="session-main"><strong>{{ p.kind === 'Refund' ? 'Refund' : 'Fee payment' }} · {{ p.receipt }}</strong><small>{{ p.method }} · {{ p.paidOn | day }}</small></div>
              <div class="session-pay">
                <b [class.negative]="p.kind === 'Refund'">{{ p.kind === 'Refund' ? '−' : '' }}{{ p.amount | money: true }}</b>
                <a class="text-button" [routerLink]="['/student/receipts', p.id]">Receipt →</a>
              </div>
            </article>
          } @empty {
            <div class="teacher-empty"><div class="art" aria-hidden="true">₹</div><strong>No payments yet</strong></div>
          }
          @for (a of d.adjustments; track $index) {
            <article class="session-row">
              <div class="session-main"><strong>{{ a.kind }}</strong><small>{{ a.reason }}</small></div>
              <div class="session-pay"><b>{{ a.kind === 'Discount' ? '−' : '+' }}{{ a.amount | money: true }}</b></div>
            </article>
          }
        </section>

        <section class="teacher-panel">
          <div class="panel-head">
            <h2 class="teacher-h2">Latest test results</h2>
            <a class="text-button" routerLink="/student/report-card">Full report card →</a>
          </div>
          @for (t of d.recentTests; track $index) {
            <article class="session-row">
              <div class="session-date"><b>{{ t.testDate.slice(8, 10) }}</b><small>{{ shortMonth(t.testDate) }}</small></div>
              <div class="session-main"><strong>{{ t.title }}</strong><small>{{ t.subject }}</small></div>
              <div class="session-pay"><b [class.negative]="t.percent < 40">{{ t.marks }} / {{ t.maxMarks }}</b><small>{{ t.percent }}%</small></div>
            </article>
          } @empty {
            <div class="teacher-empty"><div class="art" aria-hidden="true">☑</div><strong>No test results yet</strong></div>
          }
        </section>
      </div>

      <p class="muted gap-top">Something wrong with your details or fees? Contact the academy office.</p>
    }
  `,
})
export class StudentDashboardComponent {
  private readonly api = inject(Api);
  protected readonly data = signal<StudentPortalOverview | null>(null);
  protected readonly error = signal('');

  constructor() {
    effect(() => {
      portalRefresh();
      untracked(() =>
        this.api.get<StudentPortalOverview>('me/student').subscribe({
          next: d => {
            this.error.set('');
            this.data.set(d);
          },
          error: e => this.error.set(errorMessage(e)),
        }),
      );
    });
  }

  protected shortMonth(date: string): string {
    return new Date(`${date}T00:00:00Z`).toLocaleDateString('en-IN', { month: 'short', timeZone: 'UTC' });
  }
}
