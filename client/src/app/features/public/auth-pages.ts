import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';

const MIN_PASSWORD = 12;

@Component({
  selector: 'app-login',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="login-page">
      <div class="login-intro">
        <a class="logo-line" routerLink="/"><span class="brand-mark">A</span><strong>AcademyFlow</strong></a>
        <h1>Run your academy<br /><span>with clarity.</span></h1>
        <p>Manage students, fee collection, faculty payouts and expenses from one connected workspace.</p>
      </div>
      <form class="login-card" (ngSubmit)="submit()">
        <div class="mobile-logo"><span class="brand-mark">A</span> AcademyFlow</div>
        <h2>Welcome back</h2>
        <p>Sign in with the details your academy administrator gave you.</p>
        @if (error()) {
          <div class="alert" role="alert">{{ error() }}</div>
        }
        <label for="code">Academy code</label>
        <input id="code" name="code" [(ngModel)]="academyCode" placeholder="e.g. brightminds" autocomplete="organization" autocapitalize="none" />
        <div class="helper">Platform administrators leave this blank.</div>
        <label for="email">Email address</label>
        <input id="email" name="email" type="email" [(ngModel)]="email" placeholder="name@academy.com" autocomplete="username" required />
        <label for="password">Password</label>
        <input id="password" name="password" type="password" [(ngModel)]="password" autocomplete="current-password" required />
        <button type="submit" class="primary wide" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign in →' }}</button>
        <div class="private-note"><a class="text-button" routerLink="/forgot-password">Forgot your password?</a></div>
      </form>
    </div>
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected academyCode = this.auth.rememberedAcademyCode();
  protected email = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async submit(): Promise<void> {
    if (!this.email.trim() || !this.password) {
      this.error.set('Enter your email and password.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.login(this.academyCode, this.email, this.password);
      await this.router.navigateByUrl(this.auth.homeUrl());
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}

@Component({
  selector: 'app-forgot-password',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="login-page single">
      <form class="login-card" (ngSubmit)="submit()">
        <h2>Reset your password</h2>
        @if (sent()) {
          <div class="success">If that account exists, a reset link is on its way. It works once and expires in 60 minutes.</div>
        } @else {
          <p>Enter your academy code and email. We will send a link to choose a new password.</p>
          @if (error()) {
            <div class="alert" role="alert">{{ error() }}</div>
          }
          <label for="code">Academy code</label>
          <input id="code" name="code" [(ngModel)]="academyCode" autocapitalize="none" />
          <label for="email">Email address</label>
          <input id="email" name="email" type="email" [(ngModel)]="email" required />
          <button type="submit" class="primary wide" [disabled]="busy()">{{ busy() ? 'Sending…' : 'Send reset link' }}</button>
        }
        <div class="private-note"><a class="text-button" routerLink="/login">← Back to sign in</a></div>
      </form>
    </div>
  `,
})
export class ForgotPasswordComponent {
  private readonly api = inject(Api);
  protected academyCode = inject(AuthService).rememberedAcademyCode();
  protected email = '';
  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal('');

  protected submit(): void {
    if (!this.email.trim()) {
      this.error.set('Enter your email address.');
      return;
    }
    this.busy.set(true);
    this.api.post('auth/forgot-password', { academyCode: this.academyCode.trim() || null, email: this.email }).subscribe({
      next: () => this.sent.set(true),
      error: error => {
        this.error.set(errorMessage(error));
        this.busy.set(false);
      },
    });
  }
}

@Component({
  selector: 'app-reset-password',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="login-page single">
      <form class="login-card" (ngSubmit)="submit()">
        <h2>Choose a new password</h2>
        @if (done()) {
          <div class="success">Your password has been changed.</div>
          <a class="primary wide" routerLink="/login">Go to sign in →</a>
        } @else {
          @if (error()) {
            <div class="alert" role="alert">{{ error() }}</div>
          }
          <label for="password">New password (at least 12 characters)</label>
          <input id="password" name="password" type="password" [(ngModel)]="password" autocomplete="new-password" required />
          <label for="confirm">Repeat the new password</label>
          <input id="confirm" name="confirm" type="password" [(ngModel)]="confirm" autocomplete="new-password" required />
          <button type="submit" class="primary wide" [disabled]="busy()">{{ busy() ? 'Saving…' : 'Change password' }}</button>
        }
      </form>
    </div>
  `,
})
export class ResetPasswordComponent {
  private readonly api = inject(Api);
  /** Bound from the ?token= query parameter in the emailed link. */
  readonly token = input('');
  protected password = '';
  protected confirm = '';
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly error = signal('');

  protected submit(): void {
    if (this.password.length < MIN_PASSWORD) return this.error.set('Password must contain at least 12 characters.');
    if (this.password !== this.confirm) return this.error.set('The two passwords do not match.');

    this.busy.set(true);
    this.api.post('auth/reset-password', { token: this.token(), password: this.password }).subscribe({
      next: () => this.done.set(true),
      error: error => {
        this.error.set(errorMessage(error));
        this.busy.set(false);
      },
    });
  }
}

/** Shown to academy users who cannot fix a lapsed subscription themselves. */
@Component({
  selector: 'app-locked',
  template: `
    <div class="login-page single">
      <section class="login-card">
        <h2>Access is paused</h2>
        <p>{{ auth.access()?.reason ?? 'This academy does not have an active subscription.' }}</p>
        <p>Please ask your academy's Super Admin to sort out the subscription. Your data is safe.</p>
        <button type="button" class="secondary wide" (click)="signOut()">Sign out</button>
      </section>
    </div>
  `,
})
export class LockedComponent {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
