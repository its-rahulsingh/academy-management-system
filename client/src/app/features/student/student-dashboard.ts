import { Component, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, errorMessage } from '../../core/api';
import { StudentPortalOverview } from '../../core/models';
import { UI } from '../../shared/ui';
import { portalRefresh } from '../teacher/teacher-shell';

/**
 * A student's own page: their course, subjects and teachers, and latest test results.
 * Nothing about fees: receipts are emailed to the student when a payment is recorded,
 * and the agreed fee stays with the office.
 */
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
          <p>{{ d.student.courseName }} · {{ d.student.academicYearName }} · joined {{ d.student.admissionDate | day }}</p>
        </div>
        <div class="hero-art" aria-hidden="true"><span>☆</span></div>
      </section>

      <div class="teacher-stats three">
        <article class="tstat dark">
          <div class="tstat-top">SUBJECTS<span>❖</span></div>
          <div class="tstat-value">{{ d.subjects.length }}</div>
          <small>{{ d.student.courseName }}</small>
        </article>
        <article class="tstat sage">
          <div class="tstat-top">LATEST TEST<span>☑</span></div>
          @if (d.recentTests[0]; as t) {
            <div class="tstat-value">{{ t.percent }}%</div>
            <small>{{ t.title }} · {{ t.subject }}</small>
          } @else {
            <div class="tstat-value">—</div>
            <small>No tests yet</small>
          }
        </article>
        <article class="tstat sky">
          <div class="tstat-top">FEE RECEIPTS<span>✉</span></div>
          <div class="tstat-value small">By email</div>
          <small>{{ d.student.email ? 'Sent to ' + d.student.email : 'Ask the office to add your email' }}</small>
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

      <p class="muted gap-top">Fee receipts are emailed to you each time a payment is recorded. For anything about fees, contact the academy office.</p>
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
