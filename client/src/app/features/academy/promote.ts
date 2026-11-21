import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api, Notify, errorMessage } from '../../core/api';
import { PromotionCandidates } from '../../core/models';
import { UI } from '../../shared/ui';

interface Row {
  id: string;
  fullName: string;
  courseName: string;
  due: number;
  active: boolean;
  promotedTo: string | null;
  picked: boolean;
  courseId: string;
  fee: number;
}

/**
 * Moves continuing students into the next academic year: tick who is continuing, set their new course and fee.
 * Each gets a new record in the new year (linked to last year's), and their portal login moves with them.
 */
@Component({
  selector: 'app-promote',
  imports: [UI, RouterLink],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">STUDENTS</div>
        <h1>Promote to next year</h1>
        <p>Tick the students who are continuing and choose their new course and fee. Last year's records, payments and any dues stay as they are.</p>
      </div>
      <div class="page-actions"><a class="secondary" routerLink="/app/students">← Students</a></div>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (data(); as d) {
      <div class="filter-bar">
        <label class="inline">From
          <select [ngModel]="d.from.id" (ngModelChange)="load($event, courseFilter())">
            @for (y of d.years; track y.id) {
              <option [value]="y.id">{{ y.name }}</option>
            }
          </select>
        </label>
        <label class="inline">Course
          <select [ngModel]="courseFilter()" (ngModelChange)="load(d.from.id, $event)">
            <option value="">All courses</option>
            @for (c of d.courses; track c.id) {
              <option [value]="c.id">{{ c.name }}</option>
            }
          </select>
        </label>
        <span class="muted">Into: <b>{{ d.to?.name ?? '—' }}</b></span>
      </div>

      @if (!d.to) {
        <div class="alert" role="status">
          There is no academic year after {{ d.from.name }} yet. A Super Admin can add it under
          <a routerLink="/app/academic-years">Academic years</a>, then come back here.
        </div>
      } @else {
        <section class="surface">
          <div class="surface-title">
            <h2>{{ d.from.name }} → {{ d.to.name }}</h2>
            <div class="button-row tight">
              <button type="button" class="text-button" (click)="tickAll(true)">Tick all</button>
              <button type="button" class="text-button" (click)="tickAll(false)">Clear</button>
            </div>
          </div>
          <div class="filter-bar">
            <label class="inline">New course for ticked students
              <select #bulkCourse>
                @for (c of d.courses; track c.id) {
                  <option [value]="c.id">{{ c.name }} — {{ c.fee | money }}</option>
                }
              </select>
            </label>
            <button type="button" class="secondary" (click)="applyCourse(bulkCourse.value)">Apply to ticked</button>
          </div>
          <div class="table-scroll">
            <table>
              <thead><tr><th></th><th>STUDENT</th><th>THIS YEAR</th><th>NEW COURSE</th><th>NEW FEE (₹)</th></tr></thead>
              <tbody>
                @for (r of rows(); track r.id) {
                  <tr [class.dimmed]="!!r.promotedTo">
                    <td><input type="checkbox" [ngModel]="r.picked" (ngModelChange)="r.picked = $event; tick.update(n => n + 1)" [disabled]="!!r.promotedTo" [attr.aria-label]="'Promote ' + r.fullName" /></td>
                    <td>
                      <strong>{{ r.fullName }}</strong>
                      @if (r.promotedTo) {
                        <small>Already promoted to {{ r.promotedTo }}</small>
                      } @else if (!r.active) {
                        <small>Inactive</small>
                      }
                    </td>
                    <td>{{ r.courseName }}@if (r.due > 0) {<small class="negative">{{ r.due | money }} still due — stays collectable</small>}</td>
                    <td>
                      <select [(ngModel)]="r.courseId" (ngModelChange)="courseChanged(r)" [disabled]="!!r.promotedTo">
                        @for (c of d.courses; track c.id) {
                          <option [value]="c.id">{{ c.name }}</option>
                        }
                      </select>
                    </td>
                    <td><input type="number" min="0" step="0.01" [(ngModel)]="r.fee" [disabled]="!!r.promotedTo" /></td>
                  </tr>
                } @empty {
                  <tr><td colspan="5" class="empty">No students in {{ d.from.name }}{{ courseFilter() ? ' for this course' : '' }}.</td></tr>
                }
              </tbody>
            </table>
          </div>
          <label class="check gap-top">
            <input type="checkbox" [(ngModel)]="markOthers" />
            Mark the students I did not tick as inactive (they have left)
          </label>
          <div class="button-row gap-top">
            <button type="button" class="primary" [disabled]="busy()" (click)="promote()">
              Promote {{ pickedCount() }} {{ pickedCount() === 1 ? 'student' : 'students' }} to {{ d.to.name }}
            </button>
          </div>
          <p class="helper">
            Each promoted student gets a new record in {{ d.to.name }}. Their portal login moves to it, their report card keeps
            last year's tests, and anything they still owe for {{ d.from.name }} is shown on the new record and can still be collected.
          </p>
        </section>
      }
    }
  `,
})
export class PromoteComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  private readonly router = inject(Router);
  protected readonly data = signal<PromotionCandidates | null>(null);
  protected readonly rows = signal<Row[]>([]);
  protected readonly courseFilter = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  /** Bumped when a tick box changes, so the count on the button updates. */
  protected readonly tick = signal(0);
  protected readonly pickedCount = computed(() => {
    this.tick();
    return this.rows().filter(r => r.picked).length;
  });
  protected markOthers = false;

  constructor() {
    this.load(null, '');
  }

  protected load(fromYearId: string | null, courseId: string): void {
    this.courseFilter.set(courseId ?? '');
    this.api.get<PromotionCandidates>('students/promotion', { fromYearId, courseId: courseId || null }).subscribe({
      next: d => {
        this.error.set('');
        this.data.set(d);
        const fees = new Map(d.courses.map(c => [c.id, c.fee]));
        this.rows.set(d.students.map(s => ({
          id: s.id, fullName: s.fullName, courseName: s.courseName, due: s.due, active: s.active, promotedTo: s.promotedTo,
          picked: false, courseId: s.courseId, fee: fees.get(s.courseId) ?? s.feeAmount,
        })));
      },
      error: e => this.error.set(errorMessage(e)),
    });
  }

  protected tickAll(on: boolean): void {
    this.rows.update(rows => rows.map(r => ({ ...r, picked: on && !r.promotedTo })));
  }

  protected applyCourse(courseId: string): void {
    const fee = this.data()?.courses.find(c => c.id === courseId)?.fee ?? 0;
    this.rows.update(rows => rows.map(r => (r.picked ? { ...r, courseId, fee } : r)));
  }

  protected courseChanged(r: Row): void {
    r.fee = this.data()?.courses.find(c => c.id === r.courseId)?.fee ?? r.fee;
  }

  protected promote(): void {
    const d = this.data();
    if (!d?.to) return;
    const picked = this.rows().filter(r => r.picked);
    if (!picked.length && !this.markOthers) return this.error.set('Tick the students who are continuing.');
    if (picked.some(r => !(Number(r.fee) >= 0))) return this.error.set('Enter a valid fee for every ticked student.');
    this.busy.set(true);
    this.api.post<{ promoted: number; deactivated: number; toYear: string }>('students/promote', {
      fromYearId: d.from.id,
      toYearId: d.to.id,
      courseId: this.courseFilter() || null,
      students: picked.map(r => ({ studentId: r.id, courseId: r.courseId, feeAmount: Number(r.fee) })),
      markOthersInactive: this.markOthers,
    }).subscribe({
      next: r => {
        this.busy.set(false);
        this.notify.success(`${r.promoted} promoted to ${r.toYear}` + (r.deactivated ? `, ${r.deactivated} marked inactive` : ''));
        this.load(d.from.id, this.courseFilter());
      },
      error: e => {
        this.busy.set(false);
        this.error.set(errorMessage(e));
      },
    });
  }
}
