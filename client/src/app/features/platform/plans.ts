import { Component, inject, signal } from '@angular/core';
import { Api, FormState, Notify, errorMessage } from '../../core/api';
import { BillingInterval, Plan } from '../../core/models';
import { UI } from '../../shared/ui';

@Component({
  selector: 'app-plans',
  imports: [UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">PLATFORM</div>
        <h1>Plans</h1>
        <p>What you sell, at what price, and the limits that come with it.</p>
      </div>
      <button type="button" class="primary" (click)="open()">+ New plan</button>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    <div class="course-grid">
      @for (plan of plans(); track plan.id) {
        <article class="surface course-card">
          <div class="course-title">
            <h3>{{ plan.name }}</h3>
            <app-status [value]="plan.isActive ? 'Active' : 'Inactive'" />
          </div>
          <div class="course-number">{{ plan.price | money }}<small>PER {{ plan.interval === 'Monthly' ? 'MONTH' : plan.interval === 'Quarterly' ? 'QUARTER' : 'YEAR' }}</small></div>
          <p class="muted">{{ plan.description }}</p>
          <div class="course-bottom">
            <div><strong>{{ plan.maxStudents ?? '∞' }}</strong><small>STUDENTS</small></div>
            <div><strong>{{ plan.maxUsers ?? '∞' }}</strong><small>USERS</small></div>
            <div><strong>{{ plan.subscribers }}</strong><small>ACADEMIES</small></div>
            <button type="button" class="table-action push-right" (click)="open(plan)">Edit →</button>
          </div>
        </article>
      } @empty {
        <div class="surface empty">No plans yet.</div>
      }
    </div>

    @if (form.open()) {
      <app-modal [title]="model.id ? 'Edit plan' : 'New plan'" [busy]="form.busy()" [error]="form.error()" (closed)="form.close()" (saved)="save()">
        <div class="two-fields">
          <div><label for="name">Name *</label><input id="name" [(ngModel)]="model.name" maxlength="150" /></div>
          <div><label for="code">Code *</label><input id="code" [(ngModel)]="model.code" maxlength="40" [disabled]="!!model.id" autocapitalize="none" /></div>
        </div>
        <label for="description">Description</label>
        <input id="description" [(ngModel)]="model.description" maxlength="500" />
        <div class="two-fields">
          <div><label for="price">Price (₹) *</label><input id="price" type="number" min="0" step="0.01" [(ngModel)]="model.price" /></div>
          <div>
            <label for="interval">Billed</label>
            <select id="interval" [(ngModel)]="model.interval">
              @for (i of intervals; track i) {
                <option>{{ i }}</option>
              }
            </select>
          </div>
        </div>
        <div class="two-fields">
          <div><label for="maxStudents">Student limit</label><input id="maxStudents" type="number" min="1" [(ngModel)]="model.maxStudents" placeholder="Unlimited" /></div>
          <div><label for="maxUsers">User limit</label><input id="maxUsers" type="number" min="1" [(ngModel)]="model.maxUsers" placeholder="Unlimited" /></div>
        </div>
        <label class="check"><input type="checkbox" [(ngModel)]="model.isActive" /> Available for new academies</label>
        <div class="helper">Changing the price affects new academies only. Existing subscriptions keep the price they signed up at until you change it on the academy.</div>
      </app-modal>
    }
  `,
})
export class PlansComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  protected readonly intervals: BillingInterval[] = ['Monthly', 'Quarterly', 'Yearly'];
  protected readonly plans = signal<Plan[]>([]);
  protected readonly error = signal('');
  protected readonly form = new FormState();
  protected model = this.blank();

  constructor() {
    this.load();
  }

  private load(): void {
    this.api.get<Plan[]>('platform/plans').subscribe({ next: plans => this.plans.set(plans), error: e => this.error.set(errorMessage(e)) });
  }

  private blank() {
    return {
      id: '', code: '', name: '', description: '', price: 0, interval: 'Monthly' as BillingInterval,
      maxStudents: null as number | null, maxUsers: null as number | null, isActive: true,
    };
  }

  protected open(plan?: Plan): void {
    this.model = plan
      ? { id: plan.id, code: plan.code, name: plan.name, description: plan.description, price: plan.price, interval: plan.interval, maxStudents: plan.maxStudents, maxUsers: plan.maxUsers, isActive: plan.isActive }
      : this.blank();
    this.form.show();
  }

  protected save(): void {
    const { id, ...body } = this.model;
    if (!body.name.trim()) return this.form.fail('Enter a plan name.');
    // An emptied number input yields '' or null; both mean "no limit".
    const payload = { ...body, maxStudents: body.maxStudents || null, maxUsers: body.maxUsers || null };
    this.form.submit(id ? this.api.put(`platform/plans/${id}`, payload) : this.api.post('platform/plans', payload), () => {
      this.notify.success('Plan saved');
      this.load();
    });
  }
}
