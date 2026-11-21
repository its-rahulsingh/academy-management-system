import { Component, inject, signal } from '@angular/core';
import { Api, FormState, Notify, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { AppUser, Role, UserList } from '../../core/models';
import { UI } from '../../shared/ui';

const ROLE_HELP: Record<string, string> = {
  SuperAdmin: 'Everything, including the subscription and other Super Admins.',
  Admin: 'Everything except the subscription. Cannot manage Super Admins.',
  Accountant: 'Fees, refunds, expenses, payouts and reports. No user management.',
  Staff: 'Students, fee collection and test marks. No expenses, payouts or totals.',
  Faculty: 'Faculty portal only: clocks in and out of classes and sees their own pay. Created from the Faculty screen.',
  Student: 'Student portal only: sees their own fees, receipts and test results. Created from the student record.',
};

@Component({
  selector: 'app-users',
  imports: [UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Users</h1>
        <p>Who can sign in to this academy, and what they can do.</p>
      </div>
      <button type="button" class="primary" (click)="openCreate()">+ Create user</button>
    </div>

    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    <div class="surface table-scroll">
      <table>
        <thead><tr><th>USER</th><th>ROLE</th><th>LAST SIGN-IN</th><th>STATUS</th><th></th></tr></thead>
        <tbody>
          @for (u of users(); track u.id) {
            <tr [class.dimmed]="!u.active">
              <td><strong>{{ u.displayName }}{{ u.id === me ? ' (you)' : '' }}</strong><small>{{ u.email }}</small></td>
              <td>{{ u.role }}</td>
              <td>{{ u.lastLoginAtUtc | day }}</td>
              <td><app-status [value]="u.active ? 'Active' : 'Disabled'" /></td>
              <td class="row-actions">
                @if (u.canManage) {
                  <button type="button" class="table-action" (click)="openEdit(u)">Edit</button>
                  <button type="button" class="table-action" (click)="openPassword(u)">Reset password</button>
                  @if (u.id !== me) {
                    <button type="button" class="table-action" [class.danger-text]="u.active" (click)="toggle(u)">{{ u.active ? 'Disable' : 'Enable' }}</button>
                  }
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="5" class="empty">No users found.</td></tr>
          }
        </tbody>
      </table>
    </div>

    @if (form.open()) {
      <app-modal [title]="model.id ? 'Edit user' : 'Create user'" [busy]="form.busy()" [error]="form.error()" (closed)="form.close()" (saved)="save()">
        <label for="name">Full name *</label>
        <input id="name" [(ngModel)]="model.displayName" maxlength="140" />
        @if (!model.id) {
          <label for="email">Email *</label>
          <input id="email" type="email" [(ngModel)]="model.email" />
        }
        @if (model.role === 'Faculty' || model.role === 'Student') {
          <label>Role</label>
          <p class="helper">{{ model.role }} — {{ roleHelp[model.role] }}</p>
        } @else {
          <label for="role">Role *</label>
          <select id="role" [(ngModel)]="model.role">
            @for (role of roles(); track role) {
              <option>{{ role }}</option>
            }
          </select>
          <div class="helper">{{ roleHelp[model.role] }}</div>
        }
        @if (!model.id) {
          <label for="password">Initial password * (at least 5 characters)</label>
          <input id="password" type="password" autocomplete="new-password" [(ngModel)]="model.password" />
          <div class="helper">It is emailed to the user with the sign-in details. They must choose their own password at first sign-in.</div>
        }
      </app-modal>
    }

    @if (password.open()) {
      <app-modal [title]="'Reset password — ' + passwordFor()?.displayName" saveLabel="Set password" [busy]="password.busy()" [error]="password.error()" (closed)="password.close()" (saved)="savePassword()">
        <label for="newPassword">New password * (at least 5 characters)</label>
        <input id="newPassword" type="password" autocomplete="new-password" [(ngModel)]="newPassword" />
        <div class="helper">This signs the user out everywhere and emails them the new password. They must choose their own password at next sign-in.</div>
      </app-modal>
    }
  `,
})
export class UsersComponent {
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  protected readonly me = inject(AuthService).user()?.id;
  protected readonly roleHelp = ROLE_HELP;

  protected readonly users = signal<AppUser[]>([]);
  protected readonly roles = signal<Role[]>([]);
  protected readonly error = signal('');
  protected readonly form = new FormState();
  protected model = { id: '', displayName: '', email: '', role: 'Staff' as Role, password: '' };
  protected readonly password = new FormState();
  protected readonly passwordFor = signal<AppUser | null>(null);
  protected newPassword = '';

  constructor() {
    this.load();
  }

  private load(): void {
    this.api.get<UserList>('users').subscribe({
      next: list => {
        this.users.set(list.items);
        this.roles.set(list.assignableRoles);
      },
      error: e => this.error.set(errorMessage(e)),
    });
  }

  protected openCreate(): void {
    this.model = { id: '', displayName: '', email: '', role: 'Staff', password: '' };
    this.form.show();
  }

  protected openEdit(user: AppUser): void {
    this.model = { id: user.id, displayName: user.displayName, email: user.email, role: user.role, password: '' };
    this.form.show();
  }

  protected save(): void {
    const { id, displayName, email, role, password } = this.model;
    if (!displayName.trim()) return this.form.fail('Enter a name.');
    if (!id && password.length < 5) return this.form.fail('Password must contain at least 5 characters.');

    const request = id ? this.api.put(`users/${id}`, { displayName, role }) : this.api.post('users', { displayName, email, role, password });
    this.form.submit(request, () => {
      this.notify.success(id ? 'User updated' : 'User created');
      this.load();
    });
  }

  protected openPassword(user: AppUser): void {
    this.passwordFor.set(user);
    this.newPassword = '';
    this.password.show();
  }

  protected savePassword(): void {
    const user = this.passwordFor();
    if (!user) return;
    if (this.newPassword.length < 5) return this.password.fail('Password must contain at least 5 characters.');
    this.password.submit(this.api.put(`users/${user.id}/password`, { password: this.newPassword }), () => this.notify.success('Password reset'));
  }

  protected toggle(user: AppUser): void {
    if (!confirm(`${user.active ? 'Disable' : 'Enable'} ${user.email}?`)) return;
    this.api.put(`users/${user.id}/active`, { active: !user.active }).subscribe({
      next: () => {
        this.notify.success(user.active ? 'User disabled' : 'User enabled');
        this.load();
      },
      error: e => this.error.set(errorMessage(e)),
    });
  }
}
