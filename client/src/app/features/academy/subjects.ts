import { Component, computed, inject, input, model, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, FormState, Notify, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { Permission, Subject, SubjectRef } from '../../core/models';
import { UI } from '../../shared/ui';

/**
 * Tick boxes for choosing subjects from the master. Offers active subjects, plus any inactive ones already
 * chosen (so editing a course never silently drops them).
 */
@Component({
  selector: 'app-subject-picker',
  imports: [RouterLink],
  template: `
    @if (options().length) {
      <div class="subject-picker" role="group" [attr.aria-label]="label()">
        @for (s of options(); track s.id) {
          <label class="subject-chip" [class.selected]="isSelected(s.id)">
            <input type="checkbox" [checked]="isSelected(s.id)" (change)="toggle(s.id)" />
            {{ s.name }}@if (!s.active) { <small>(inactive)</small> }
          </label>
        }
      </div>
    } @else {
      <p class="helper">
        No subjects in the master yet.
        @if (canManage) { <a routerLink="/app/subjects">Add subjects →</a> }
      </p>
    }
  `,
})
export class SubjectPickerComponent {
  readonly subjects = input.required<SubjectRef[]>();
  readonly selected = model<string[]>([]);
  readonly label = input('Subjects');
  protected readonly canManage = inject(AuthService).can(Permission.CoursesWrite);

  protected readonly options = computed(() => this.subjects().filter(s => s.active || this.selected().includes(s.id)));

  protected isSelected(id: string): boolean {
    return this.selected().includes(id);
  }

  protected toggle(id: string): void {
    this.selected.update(ids => (ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id]));
  }
}

/** The subject master: the subjects the academy teaches. */
@Component({
  selector: 'app-subjects',
  imports: [UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Subjects</h1>
        <p>The subjects your academy teaches. Link them to courses and faculty; every test is set in one.</p>
      </div>
      @if (canEdit) {
        <button type="button" class="primary" (click)="open()">+ Add subject</button>
      }
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    <div class="surface table-scroll">
      <table>
        <thead><tr><th>SUBJECT</th><th>CODE</th><th>COURSES</th><th>FACULTY</th><th>TESTS</th><th>STATUS</th><th></th></tr></thead>
        <tbody>
          @for (s of subjects(); track s.id) {
            <tr [class.dimmed]="!s.active">
              <td><strong>{{ s.name }}</strong></td>
              <td>{{ s.code || '—' }}</td>
              <td>{{ s.courses }}</td>
              <td>{{ s.faculty }}</td>
              <td>{{ s.tests }}</td>
              <td><app-status [value]="s.active ? 'Active' : 'Inactive'" /></td>
              <td class="row-actions">
                @if (canEdit) {
                  <button type="button" class="table-action" (click)="open(s)">Edit →</button>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="7" class="empty">No subjects yet. Add the subjects you teach, then link them to your courses.</td></tr>
          }
        </tbody>
      </table>
    </div>

    @if (form.open()) {
      <app-modal [title]="model.id ? 'Edit subject' : 'Add subject'" [busy]="form.busy()" [error]="form.error()" (closed)="form.close()" (saved)="save()">
        <div class="two-fields">
          <div><label for="name">Subject name *</label><input id="name" [(ngModel)]="model.name" maxlength="100" placeholder="e.g. Physics" /></div>
          <div><label for="code">Short code</label><input id="code" [(ngModel)]="model.code" maxlength="12" placeholder="e.g. PHY" /></div>
        </div>
        @if (model.id) {
          <label class="check"><input type="checkbox" [(ngModel)]="model.active" /> Active</label>
          <p class="helper">An inactive subject keeps its tests and links but cannot be added to more courses, faculty or new tests. Renaming changes the name everywhere, including past tests.</p>
        }
      </app-modal>
    }
  `,
})
export class SubjectsComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  protected readonly canEdit = inject(AuthService).can(Permission.CoursesWrite);

  protected readonly subjects = signal<Subject[]>([]);
  protected readonly error = signal('');
  protected readonly form = new FormState();
  protected model = { id: '', name: '', code: '', active: true };

  constructor() {
    this.load();
  }

  private load(): void {
    this.api.get<Subject[]>('subjects').subscribe({ next: s => this.subjects.set(s), error: e => this.error.set(errorMessage(e)) });
  }

  protected open(s?: Subject): void {
    this.model = s ? { id: s.id, name: s.name, code: s.code, active: s.active } : { id: '', name: '', code: '', active: true };
    this.form.show();
  }

  protected save(): void {
    const { id, ...body } = this.model;
    if (!body.name.trim()) return this.form.fail('Enter the subject name.');
    this.form.submit(id ? this.api.put(`subjects/${id}`, body) : this.api.post('subjects', body), () => {
      this.notify.success('Subject saved');
      this.load();
    });
  }
}
