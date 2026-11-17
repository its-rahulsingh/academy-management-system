import { Component, inject } from '@angular/core';
import { AcademicYearService } from '../../core/academic-year.service';
import { Api, FormState, Notify } from '../../core/api';
import { AcademicYear } from '../../core/models';
import { UI, today } from '../../shared/ui';

/** The academy's year master. Only the Super Admin can add years or change the current or a future year. */
@Component({
  selector: 'app-academic-years',
  imports: [UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Academic years</h1>
        <p>Set the dates of your academic year. Students, fees, expenses, payouts and tests are shown one year at a time.</p>
      </div>
      @if (years.canManage()) {
        <button type="button" class="primary" (click)="open()">+ Add next year</button>
      }
    </div>

    <section class="surface">
      <p class="muted">
        A year that has ended is locked: its dates cannot be changed. Courses, faculty, users and the subscription are not tied to a year.
      </p>
      <div class="table-scroll">
        <table>
          <thead><tr><th>YEAR</th><th>STARTS</th><th>ENDS</th><th>STUDENTS ADMITTED</th><th>STATUS</th><th></th></tr></thead>
          <tbody>
            @for (y of years.years(); track y.id) {
              <tr [class.dimmed]="y.isClosed">
                <td><strong>{{ y.name }}</strong></td>
                <td>{{ y.startDate | day }}</td>
                <td>{{ y.endDate | day }}</td>
                <td>{{ y.students }}</td>
                <td><app-status [value]="y.isCurrent ? 'Current' : y.isClosed ? 'Ended' : 'Upcoming'" /></td>
                <td class="row-actions">
                  @if (years.canManage() && !y.isClosed) {
                    <button type="button" class="table-action" (click)="open(y)">Edit dates</button>
                  } @else if (y.isClosed) {
                    <small>Locked</small>
                  }
                </td>
              </tr>
            } @empty {
              <tr><td colspan="6" class="empty">No academic years yet.</td></tr>
            }
          </tbody>
        </table>
      </div>
    </section>

    @if (form.open()) {
      <app-modal [title]="model.id ? 'Edit ' + model.name : 'Add academic year'" [busy]="form.busy()" [error]="form.error()" (closed)="form.close()" (saved)="save()">
        <label for="name">Name *</label>
        <input id="name" [(ngModel)]="model.name" maxlength="40" placeholder="e.g. 2027-28" />
        <div class="two-fields">
          <div><label for="start">First day *</label><input id="start" type="date" [(ngModel)]="model.startDate" /></div>
          <div><label for="end">Last day *</label><input id="end" type="date" [min]="minEnd" [(ngModel)]="model.endDate" /></div>
        </div>
        <div class="helper">Years cannot overlap. Once the last day has passed the year is locked and these dates can no longer be changed.</div>
      </app-modal>
    }
  `,
})
export class AcademicYearsComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  protected readonly years = inject(AcademicYearService);
  protected readonly form = new FormState();
  protected readonly minEnd = today();
  protected model = { id: '', name: '', startDate: '', endDate: '' };

  protected open(year?: AcademicYear): void {
    this.model = year ? { id: year.id, name: year.name, startDate: year.startDate, endDate: year.endDate } : this.suggestNext();
    this.form.show();
  }

  /** Proposes the twelve months after the latest year, named in the usual "2027-28" style. */
  private suggestNext() {
    const latest = this.years.years().reduce<AcademicYear | null>((max, y) => (!max || y.endDate > max.endDate ? y : max), null);
    if (!latest) return { id: '', name: '', startDate: '', endDate: '' };

    const start = new Date(`${latest.endDate}T00:00:00Z`);
    start.setUTCDate(start.getUTCDate() + 1);
    const end = new Date(start);
    end.setUTCFullYear(end.getUTCFullYear() + 1);
    end.setUTCDate(end.getUTCDate() - 1);
    const first = start.getUTCFullYear();
    const name = end.getUTCFullYear() === first ? `${first}` : `${first}-${String(end.getUTCFullYear() % 100).padStart(2, '0')}`;
    return { id: '', name, startDate: start.toISOString().slice(0, 10), endDate: end.toISOString().slice(0, 10) };
  }

  protected save(): void {
    const { id, ...body } = this.model;
    if (!body.name.trim()) return this.form.fail('Enter a name for the year.');
    if (!body.startDate || !body.endDate) return this.form.fail('Enter the first and last day.');
    if (body.endDate <= body.startDate) return this.form.fail('The last day must be after the first day.');

    this.form.submit(id ? this.api.put(`academic-years/${id}`, body) : this.api.post('academic-years', body), () => {
      this.notify.success(id ? 'Academic year updated' : 'Academic year added');
      void this.years.load();
    });
  }
}
