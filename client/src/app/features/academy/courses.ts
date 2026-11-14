import { Component, computed, inject, signal } from '@angular/core';
import { Api, Notify, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { Course, Permission } from '../../core/models';
import { UI } from '../../shared/ui';

@Component({
  selector: 'app-courses',
  imports: [UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Courses</h1>
        <p>Your course catalogue, enrolment and collections.</p>
      </div>
      @if (canEdit) {
        <button type="button" class="primary" (click)="open()">+ Add course</button>
      }
    </div>

    @if (error() && !editing()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    <div class="stats three">
      <article class="stat white"><div class="stat-top">COURSES</div><div class="stat-value">{{ courses().length }}</div></article>
      <article class="stat green"><div class="stat-top">ACTIVE</div><div class="stat-value">{{ activeCount() }}</div></article>
      <article class="stat white"><div class="stat-top">ENROLLED</div><div class="stat-value">{{ enrolled() }}</div></article>
    </div>

    <div class="course-grid">
      @for (course of courses(); track course.id) {
        <article class="surface course-card">
          <div class="course-title">
            <h3>{{ course.name }}</h3>
            <app-status [value]="course.active ? 'Active' : 'Inactive'" />
          </div>
          <div class="course-number">{{ course.fee | money }}<small>COURSE FEE</small></div>
          <div class="course-bottom">
            <div><strong>{{ course.enrolled }}</strong><small>ENROLLED</small></div>
            <div><strong>{{ course.collected | money }}</strong><small>COLLECTED</small></div>
            @if (canEdit) {
              <button type="button" class="table-action push-right" (click)="open(course)">Edit →</button>
            }
          </div>
        </article>
      } @empty {
        <div class="surface empty">No courses yet. Add a course before enrolling students.</div>
      }
    </div>

    @if (editing()) {
      <app-modal [title]="form.id ? 'Edit course' : 'Add course'" [busy]="busy()" [error]="error()" (closed)="close()" (saved)="save()">
        <label for="name">Course name *</label>
        <input id="name" [(ngModel)]="form.name" maxlength="150" placeholder="e.g. 12th CET" />
        <label for="fee">Course fee (₹) *</label>
        <input id="fee" type="number" min="0" step="0.01" [(ngModel)]="form.fee" />
        <div class="helper">Used as the default for new admissions. Students already admitted keep their agreed fee.</div>
        @if (form.id) {
          <label class="check"><input type="checkbox" [(ngModel)]="form.active" /> Open for new admissions</label>
        }
      </app-modal>
    }
  `,
})
export class CoursesComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  protected readonly canEdit = inject(AuthService).can(Permission.CoursesWrite);

  protected readonly courses = signal<Course[]>([]);
  protected readonly editing = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly activeCount = computed(() => this.courses().filter(c => c.active).length);
  protected readonly enrolled = computed(() => this.courses().reduce((sum, c) => sum + c.enrolled, 0));
  protected form = { id: '', name: '', fee: 0, active: true };

  constructor() {
    this.load();
  }

  private load(): void {
    this.api.get<Course[]>('courses').subscribe({ next: courses => this.courses.set(courses), error: e => this.error.set(errorMessage(e)) });
  }

  protected open(course?: Course): void {
    this.form = course ? { id: course.id, name: course.name, fee: course.fee, active: course.active } : { id: '', name: '', fee: 0, active: true };
    this.error.set('');
    this.editing.set(true);
  }

  protected close(): void {
    this.editing.set(false);
    this.error.set('');
  }

  protected save(): void {
    const { id, ...body } = this.form;
    this.busy.set(true);
    (id ? this.api.put(`courses/${id}`, body) : this.api.post('courses', body)).subscribe({
      next: () => {
        this.busy.set(false);
        this.close();
        this.notify.success('Course saved');
        this.load();
      },
      error: e => {
        this.busy.set(false);
        this.error.set(errorMessage(e));
      },
    });
  }
}
