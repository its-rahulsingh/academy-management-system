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
          <p class="muted small">
            {{ plan.maxReceiptEmailsPerMonth === null ? 'Unlimited' : plan.maxReceiptEmailsPerMonth }} emailed receipts a month ·
            {{ plan.customBranding ? 'Own logo' : 'Ojas branding' }}
          </p>
          <div class="course-bottom">
            <div><strong>{{ plan.maxStudents ?? '∞' }}</strong><small>STUDENTS</small></div>
            <div><strong>{{ plan.maxFaculty ?? '∞' }}</strong><small>FACULTY</small></div>
            <div><strong>{{ plan.maxUsers ?? '∞' }}</strong><small>STAFF</small></div>
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
          <div><label for="maxUsers">Staff login limit</label><input id="maxUsers" type="number" min="1" [(ngModel)]="model.maxUsers" placeholder="Unlimited" /></div>
        </div>
        <div class="two-fields">
          <div><label for="maxFaculty">Faculty limit</label><input id="maxFaculty" type="number" min="1" [(ngModel)]="model.maxFaculty" placeholder="Unlimited" /></div>
          <div><label for="maxEmails">Emailed receipts a month</label><input id="maxEmails" type="number" min="0" [(ngModel)]="model.maxReceiptEmailsPerMonth" placeholder="Unlimited" /></div>
        </div>
        <label class="check"><input type="checkbox" [(ngModel)]="model.customBranding" /> Academy's own logo (hides "Powered by Ojas")</label>
        <label class="check"><input type="checkbox" [(ngModel)]="model.isActive" /> Available for new academies</label>
        <div class="helper">
          Limit changes apply right away to every academy on this plan; nothing is ever deleted, academies over a new limit just cannot add more.
          Price changes affect new academies only; existing subscriptions keep their price until you change it on the academy.
        </div>
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
      maxStudents: null as number | null, maxUsers: null as number | null, maxFaculty: null as number | null,
      maxReceiptEmailsPerMonth: null as number | null, customBranding: true, isActive: true,
    };
  }

  protected open(plan?: Plan): void {
    this.model = plan
      ? { id: plan.id, code: plan.code, name: plan.name, description: plan.description, price: plan.price, interval: plan.interval, maxStudents: plan.maxStudents, maxUsers: plan.maxUsers,
          maxFaculty: plan.maxFaculty, maxReceiptEmailsPerMonth: plan.maxReceiptEmailsPerMonth, customBranding: plan.customBranding, isActive: plan.isActive }
      : this.blank();
    this.form.show();
  }

  protected save(): void {
    const { id, ...body } = this.model;
    if (!body.name.trim()) return this.form.fail('Enter a plan name.');
    // An emptied number input yields '' or null; both mean "no limit".
    // Receipts allow 0 (no emailed receipts), so only an empty field means unlimited there.
    const emails = body.maxReceiptEmailsPerMonth;
    const payload = {
      ...body, maxStudents: body.maxStudents || null, maxUsers: body.maxUsers || null, maxFaculty: body.maxFaculty || null,
      maxReceiptEmailsPerMonth: emails === null || (emails as unknown) === '' ? null : emails,
    };
    this.form.submit(id ? this.api.put(`platform/plans/${id}`, payload) : this.api.post('platform/plans', payload), () => {
      this.notify.success('Plan saved');
      this.load();
    });
  }
}
