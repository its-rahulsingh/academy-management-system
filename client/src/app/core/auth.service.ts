import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ANONYMOUS, Session } from './models';

const ACADEMY_CODE_KEY = 'academyflow.academyCode';

/** Holds who is signed in. Everything that depends on identity reads the `session` signal. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly session = signal<Session>(ANONYMOUS);
  readonly user = computed(() => this.session().user);
  readonly tenant = computed(() => this.session().tenant);
  readonly access = computed(() => this.session().access);
  readonly isOwner = computed(() => this.session().user?.role === 'Owner');
  /** True when an academy user's subscription lets them use the product. */
  readonly hasAccess = computed(() => this.session().access?.allowed === true);

  can(permission: string): boolean {
    return this.session().permissions.includes(permission);
  }

  /** Where a signed-in user belongs. */
  homeUrl(): string {
    if (!this.session().authenticated) return '/';
    if (this.isOwner()) return '/platform';
    if (this.hasAccess()) return '/app/dashboard';
    return this.can('billing.manage') ? '/app/billing' : '/locked';
  }

  async refresh(): Promise<Session> {
    try {
      this.session.set(await firstValueFrom(this.http.get<Session>('/api/auth/session')));
    } catch {
      this.session.set(ANONYMOUS);
    }
    return this.session();
  }

  async login(academyCode: string, email: string, password: string): Promise<Session> {
    const session = await firstValueFrom(
      this.http.post<Session>('/api/auth/login', { academyCode: academyCode.trim() || null, email, password }),
    );
    this.rememberAcademyCode(academyCode.trim());
    this.session.set(session);
    return session;
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', {}));
    } finally {
      this.session.set(ANONYMOUS);
    }
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    this.session.set(
      await firstValueFrom(this.http.post<Session>('/api/auth/change-password', { currentPassword, newPassword })),
    );
  }

  clear(): void {
    this.session.set(ANONYMOUS);
  }

  /** The academy code is not a secret; remembering it saves staff retyping it at every sign-in. */
  rememberedAcademyCode(): string {
    try {
      return localStorage.getItem(ACADEMY_CODE_KEY) ?? '';
    } catch {
      return '';
    }
  }

  private rememberAcademyCode(code: string): void {
    try {
      if (code) localStorage.setItem(ACADEMY_CODE_KEY, code);
    } catch {
      // Storage can be unavailable (private browsing); sign-in still works without it.
    }
  }
}
