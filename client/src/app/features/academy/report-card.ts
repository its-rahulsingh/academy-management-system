import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, errorMessage } from '../../core/api';
import { StudentReportCard } from '../../core/models';
import { UI } from '../../shared/ui';

/** One student's full test history, laid out to print. Printing hides the navigation, as receipts do. */
@Component({
  selector: 'app-report-card',
  imports: [RouterLink, UI],
  template: `
    <div class="page-header no-print">
      <div>
        <a class="text-button" routerLink="/app/tests">← Tests &amp; results</a>
        <h1>Report card</h1>
      </div>
      <button type="button" class="primary" (click)="print()" [disabled]="!card()">🖶 Print</button>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (card(); as c) {
      <article class="receipt report-card surface">
        <header>
          <div>
            <h2>{{ c.academyName }}</h2>
            <p>Student report card</p>
          </div>
          <div class="receipt-number">
            <strong>{{ c.student.fullName }}</strong>
            <span>{{ c.student.courseName }} · {{ c.student.academicYear }}</span>
          </div>
        </header>

        <div class="mini-stats">
          <div><small>TESTS TAKEN</small><b>{{ c.summary.testsTaken }}</b></div>
          <div><small>OVERALL AVERAGE</small><b>{{ c.summary.averagePercent === null ? '—' : c.summary.averagePercent + '%' }}</b></div>
          <div><small>BEST</small><b class="positive">{{ c.summary.best ? c.summary.best.percent + '%' : '—' }}</b></div>
          <div><small>LOWEST</small><b class="negative">{{ c.summary.lowest ? c.summary.lowest.percent + '%' : '—' }}</b></div>
        </div>

        @if (c.tests.length) {
          <h3 class="section-title">Progress</h3>
          <div class="trend" role="img" [attr.aria-label]="'Scores over time, from ' + c.tests[0].percent + '% to ' + c.tests[c.tests.length - 1].percent + '%'">
            @for (t of c.tests; track t.testId) {
              <div class="trend-bar" [title]="t.title + ': ' + t.percent + '% (class ' + t.classAveragePercent + '%)'">
                <span class="trend-class" [style.height.%]="t.classAveragePercent"></span>
                <span class="trend-student" [class.low]="t.percent < passMark" [style.height.%]="t.percent"></span>
              </div>
            }
          </div>
          <p class="helper legend"><span class="key student"></span> Student <span class="key class"></span> Class average</p>

          <h3 class="section-title">Test history</h3>
          <div class="table-scroll">
            <table class="compact">
              <thead><tr><th>DATE</th><th>TEST</th><th>MARKS</th><th>%</th><th>CLASS AVG</th><th>RANK</th><th>REMARKS</th></tr></thead>
              <tbody>
                @for (t of c.tests; track t.testId) {
                  <tr>
                    <td>{{ t.testDate | day }}</td>
                    <td><strong>{{ t.title }}</strong><small>{{ t.subject }}</small></td>
                    <td>{{ t.marks }} / {{ t.maxMarks }}</td>
                    <td [class.positive]="t.percent >= passMark" [class.negative]="t.percent < passMark"><strong>{{ t.percent }}%</strong></td>
                    <td>{{ t.classAveragePercent }}%</td>
                    <td>{{ t.rank }} of {{ t.takers }}</td>
                    <td>{{ t.remarks || '—' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          @if (c.subjects.length > 1) {
            <h3 class="section-title">By subject</h3>
            <table class="compact">
              <tbody>
                @for (s of c.subjects; track s.subject) {
                  <tr><td>{{ s.subject }}</td><td>{{ s.tests }} test{{ s.tests === 1 ? '' : 's' }}</td><td><strong>{{ s.averagePercent }}%</strong></td></tr>
                }
              </tbody>
            </table>
          }
        } @else {
          <p class="empty">No test results recorded for this student yet.</p>
        }

        <footer>
          <span>Printed {{ printedOn }}</span>
          <span>Class teacher: ____________________</span>
        </footer>
      </article>
    } @else if (!error()) {
      <div class="empty">Loading…</div>
    }
  `,
})
export class ReportCardComponent {
  private readonly api = inject(Api);
  /** Student id from the route. */
  readonly id = input.required<string>();
  protected readonly card = signal<StudentReportCard | null>(null);
  protected readonly error = signal('');
  /** Scores below this are shown in the warning colour. */
  protected readonly passMark = 40;
  protected readonly printedOn = new Date().toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });

  constructor() {
    effect(() => {
      this.api.get<StudentReportCard>(`tests/report-cards/${this.id()}`).subscribe({
        next: card => this.card.set(card),
        error: e => this.error.set(errorMessage(e)),
      });
    });
  }

  protected print(): void {
    window.print();
  }
}
