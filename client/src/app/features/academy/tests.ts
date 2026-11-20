import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, FormState, Notify, PagedList, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { AcademyTest, Course, Paged, Permission, ReportCard, StudentLookup, SubjectRef, TestResult } from '../../core/models';
import { UI, today } from '../../shared/ui';

@Component({
  selector: 'app-tests',
  imports: [RouterLink, UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Tests &amp; results</h1>
        <p>Record marks and review each student's average.</p>
      </div>
      @if (canEdit) {
        <button type="button" class="primary" (click)="openTest()">+ Add test</button>
      }
    </div>

    @if (tests.error() || error()) {
      <div class="alert" role="alert">{{ tests.error() || error() }}</div>
    }

    <section class="surface">
      <div class="surface-title"><h2>Tests</h2></div>
      <div class="table-scroll">
        <table>
          <thead><tr><th>TEST</th><th>COURSE</th><th>DATE</th><th>MAX MARKS</th><th>RESULTS</th><th>AVERAGE</th><th></th></tr></thead>
          <tbody>
            @for (t of tests.items(); track t.id) {
              <tr>
                <td><strong>{{ t.title }}</strong><small>{{ t.subjectName }}</small></td>
                <td>{{ t.courseName }}</td>
                <td>{{ t.testDate | day }}</td>
                <td>{{ t.maxMarks }}</td>
                <td>{{ t.resultCount }}</td>
                <td>{{ t.averageMarks === null ? '—' : percent(t.averageMarks, t.maxMarks) }}</td>
                <td class="row-actions"><button type="button" class="table-action" (click)="openResults(t)">Results →</button></td>
              </tr>
            } @empty {
              <tr><td colspan="7" class="empty">{{ tests.loading() ? 'Loading…' : 'No tests yet.' }}</td></tr>
            }
          </tbody>
        </table>
      </div>
      <app-pager [page]="tests.page()" [total]="tests.total()" (pageChange)="tests.load($event)" />
    </section>

    <section class="surface">
      <div class="surface-title"><h2>Report cards</h2></div>
      <div class="filter-bar"><input placeholder="⌕  Search students…" [ngModel]="search()" (ngModelChange)="onSearch($event)" /></div>
      <div class="table-scroll">
        <table>
          <thead><tr><th>STUDENT</th><th>COURSE</th><th>TESTS TAKEN</th><th>AVERAGE</th><th></th></tr></thead>
          <tbody>
            @for (card of cards.items(); track card.studentId) {
              <tr>
                <td><strong>{{ card.fullName }}</strong></td>
                <td>{{ card.courseName }}</td>
                <td>{{ card.testsTaken }}</td>
                <td>{{ card.averagePercent === null ? 'No results yet' : round(card.averagePercent) + '%' }}</td>
                <td class="row-actions"><a class="table-action" [routerLink]="['/app/tests/report-cards', card.studentId]">Report card →</a></td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="empty">No students found.</td></tr>
            }
          </tbody>
        </table>
      </div>
      <app-pager [page]="cards.page()" [total]="cards.total()" (pageChange)="cards.load($event)" />
    </section>

    @if (form.open()) {
      <app-modal title="Add test" saveLabel="Create test" [busy]="form.busy()" [error]="form.error()" (closed)="form.close()" (saved)="saveTest()">
        <label for="title">Test title *</label>
        <input id="title" [(ngModel)]="model.title" maxlength="180" placeholder="e.g. Physics unit test 1" />
        <label for="course">Course *</label>
        <select id="course" [(ngModel)]="model.courseId" (ngModelChange)="courseChanged()">
          <option value="" disabled>Select course</option>
          @for (c of courses(); track c.id) {
            <option [value]="c.id">{{ c.name }}</option>
          }
        </select>
        <div class="two-fields">
          <div>
            <label for="subject">Subject *</label>
            <select id="subject" [(ngModel)]="model.subjectId" [disabled]="!courseSubjects().length">
              <option value="" disabled>{{ !model.courseId ? 'Select the course first' : courseSubjects().length ? 'Select subject' : 'No subjects linked' }}</option>
              @for (s of courseSubjects(); track s.id) {
                <option [value]="s.id">{{ s.name }}</option>
              }
            </select>
          </div>
          <div><label for="max">Maximum marks *</label><input id="max" type="number" min="1" [(ngModel)]="model.maxMarks" /></div>
        </div>
        @if (model.courseId && !courseSubjects().length) {
          <p class="helper">This course has no subjects yet.
            @if (canManageCourses) { <a routerLink="/app/courses">Link subjects on the Courses page →</a> } @else { Ask an administrator to link subjects to it. }
          </p>
        }
        <label for="date">Date</label>
        <input id="date" type="date" [(ngModel)]="model.testDate" />
        <label for="notes">Notes</label>
        <textarea id="notes" rows="2" maxlength="1200" [(ngModel)]="model.notes"></textarea>
      </app-modal>
    }

    @if (activeTest(); as t) {
      <app-modal [title]="t.title + ' — results'" saveLabel="" [wide]="true" [error]="resultError()" (closed)="activeTest.set(null)">
        <p class="helper">{{ t.courseName }} · {{ t.testDate | day }} · out of {{ t.maxMarks }}</p>
        <div class="table-scroll">
          <table class="compact">
            <thead><tr><th>STUDENT</th><th>MARKS</th><th>%</th><th>REMARKS</th><th></th></tr></thead>
            <tbody>
              @for (r of results(); track r.id) {
                <tr [class.editing-row]="editing()?.studentId === r.studentId">
                  <td><strong>{{ r.studentName }}</strong></td>
                  <td>{{ r.marks }} / {{ t.maxMarks }}</td>
                  <td class="positive">{{ percent(r.marks, t.maxMarks) }}</td>
                  <td>{{ r.remarks || '—' }}</td>
                  <td class="row-actions">
                    @if (canEdit) {
                      <button type="button" class="table-action" (click)="editResult(r)">Edit</button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="empty">No marks entered yet.</td></tr>
              }
            </tbody>
          </table>
        </div>

        @if (canEdit) {
          @if (editing(); as e) {
            <h3 class="section-title">Correct the result for {{ e.studentName }}</h3>
          } @else {
            <h3 class="section-title">Enter a result</h3>
            @if (remaining().length) {
              <label for="student">Student *</label>
              <select id="student" [(ngModel)]="resultModel.studentId">
                <option value="" disabled>Select student ({{ remaining().length }} without marks)</option>
                @for (s of remaining(); track s.id) {
                  <option [value]="s.id">{{ s.fullName }}</option>
                }
              </select>
            } @else {
              <p class="helper">Every student of {{ t.courseName }} in this academic year has a result. Use Edit above to correct one.</p>
            }
          }
          @if (editing() || remaining().length) {
            <div class="two-fields">
              <div><label for="marks">Marks *</label><input id="marks" type="number" min="0" [max]="t.maxMarks" step="0.5" [(ngModel)]="resultModel.marks" /></div>
              <div><label for="remarks">Remarks</label><input id="remarks" maxlength="600" [(ngModel)]="resultModel.remarks" /></div>
            </div>
            <div class="button-row">
              <button type="button" class="primary" [disabled]="savingResult()" (click)="saveResult(t)">{{ editing() ? 'Save correction' : 'Save result' }}</button>
              @if (editing()) {
                <button type="button" class="secondary" (click)="cancelEdit()">Cancel</button>
              }
            </div>
          }
        }
      </app-modal>
    }
  `,
})
export class TestsComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  private readonly auth = inject(AuthService);
  protected readonly canEdit = this.auth.can(Permission.TestsWrite);
  protected readonly canManageCourses = this.auth.can(Permission.CoursesWrite);
  /** The selected course's active subjects: a test can only be set in one of them. */
  protected readonly courseSubjects = signal<SubjectRef[]>([]);

  protected readonly error = signal('');
  protected readonly search = signal('');
  protected readonly courses = signal<Course[]>([]);
  protected readonly tests = new PagedList<AcademyTest>((page, pageSize) => this.api.get<Paged<AcademyTest>>('tests', { page, pageSize }));
  protected readonly cards = new PagedList<ReportCard>((page, pageSize) =>
    this.api.get<Paged<ReportCard>>('tests/report-cards', { page, pageSize, search: this.search() }),
  );

  protected readonly form = new FormState();
  protected model = this.blank();

  protected readonly activeTest = signal<AcademyTest | null>(null);
  protected readonly results = signal<TestResult[]>([]);
  protected readonly eligible = signal<StudentLookup[]>([]);
  /** Students of the course who have no result for this test yet; once marks are saved a student drops out of the list. */
  protected readonly remaining = computed(() => {
    const done = new Set(this.results().map(r => r.studentId));
    return this.eligible().filter(s => !done.has(s.id));
  });
  /** The result being corrected, if any. */
  protected readonly editing = signal<TestResult | null>(null);
  protected readonly resultError = signal('');
  protected readonly savingResult = signal(false);
  protected resultModel = { studentId: '', marks: 0, remarks: '' };
  private searchTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    this.tests.load();
    this.cards.load();
    this.api.get<Course[]>('courses').subscribe({ next: c => this.courses.set(c), error: e => this.error.set(errorMessage(e)) });
  }

  protected percent(marks: number, maxMarks: number): string {
    return `${Math.round((100 * marks) / maxMarks)}%`;
  }

  protected round(value: number): number {
    return Math.round(value);
  }

  protected onSearch(text: string): void {
    this.search.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.cards.reload(), 300);
  }

  private blank() {
    return { title: '', subjectId: '', courseId: '', maxMarks: 100, testDate: today(), notes: '' };
  }

  protected openTest(): void {
    this.model = this.blank();
    this.courseSubjects.set([]);
    // Reload so subjects just linked on the Courses page are offered.
    this.api.get<Course[]>('courses').subscribe({ next: c => this.courses.set(c), error: e => this.form.fail(errorMessage(e)) });
    this.form.show();
  }

  protected courseChanged(): void {
    const subjects = this.courses().find(c => c.id === this.model.courseId)?.subjects.filter(s => s.active) ?? [];
    this.courseSubjects.set(subjects);
    this.model.subjectId = subjects.length === 1 ? subjects[0].id : '';
  }

  protected saveTest(): void {
    if (!this.model.title.trim()) return this.form.fail('Enter a test title.');
    if (!this.model.courseId) return this.form.fail('Select a course.');
    if (!this.model.subjectId) return this.form.fail('Select the subject of the test.');
    this.form.submit(this.api.post('tests', this.model), () => {
      this.notify.success('Test created');
      this.tests.load();
    });
  }

  protected openResults(test: AcademyTest): void {
    this.resultModel = { studentId: '', marks: 0, remarks: '' };
    this.editing.set(null);
    this.resultError.set('');
    this.results.set([]);
    this.activeTest.set(test);
    this.loadResults(test);
    // One class at a time: ask for the whole course rather than the default short typeahead list.
    this.api.get<StudentLookup[]>('students/lookup', { courseId: test.courseId, yearOnly: true, limit: 300 }).subscribe(students => this.eligible.set(students));
  }

  private loadResults(test: AcademyTest): void {
    this.api.get<TestResult[]>(`tests/${test.id}/results`).subscribe({
      next: results => this.results.set(results),
      error: e => this.resultError.set(errorMessage(e)),
    });
  }

  protected editResult(result: TestResult): void {
    this.editing.set(result);
    this.resultError.set('');
    this.resultModel = { studentId: result.studentId, marks: result.marks, remarks: result.remarks };
  }

  protected cancelEdit(): void {
    this.editing.set(null);
    this.resultModel = { studentId: '', marks: 0, remarks: '' };
  }

  protected saveResult(test: AcademyTest): void {
    if (!this.resultModel.studentId) return this.resultError.set('Select a student.');
    this.savingResult.set(true);
    this.resultError.set('');
    const { studentId, ...body } = this.resultModel;
    this.api.put(`tests/${test.id}/results/${studentId}`, body).subscribe({
      next: () => {
        this.savingResult.set(false);
        this.resultModel = { studentId: '', marks: 0, remarks: '' };
        this.editing.set(null);
        this.loadResults(test);
        this.tests.load();
        this.cards.load();
      },
      error: e => {
        this.savingResult.set(false);
        this.resultError.set(errorMessage(e));
      },
    });
  }
}
