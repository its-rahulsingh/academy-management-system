import { HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Api } from './api';
import { AuthService } from './auth.service';
import { AcademicYear, AcademicYearList } from './models';

const STORAGE_PREFIX = 'academyflow.academicYear.';
export const YEAR_HEADER = 'X-Academic-Year';

/**
 * The academic year chosen in the global year switch. Every year-based screen reads it through the
 * request header the interceptor below adds, so screens do not have to pass it themselves.
 */
@Injectable({ providedIn: 'root' })
export class AcademicYearService {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  readonly years = signal<AcademicYear[]>([]);
  readonly currentId = signal<string | null>(null);
  readonly canManage = signal(false);
  readonly selectedId = signal<string | null>(null);
  /** False until the years have been fetched, so screens never load data for the wrong year first. */
  readonly ready = signal(false);

  readonly selected = computed(() => this.years().find(y => y.id === this.selectedId()) ?? null);
  readonly isCurrent = computed(() => !!this.selected() && this.selected()!.id === this.currentId());
  readonly isClosed = computed(() => this.selected()?.isClosed ?? false);

  async load(): Promise<void> {
    try {
      const list = await firstValueFrom(this.api.get<AcademicYearList>('academic-years'));
      this.years.set(list.items);
      this.currentId.set(list.currentId);
      this.canManage.set(list.canManage);
      // Keep the year this user last looked at, as long as it still exists; otherwise the current year.
      const remembered = this.read();
      const keep = list.items.some(y => y.id === remembered) ? remembered : list.currentId;
      this.selectedId.set(keep);
    } catch {
      this.years.set([]);
      this.selectedId.set(null);
    } finally {
      this.ready.set(true);
    }
  }

  select(id: string): void {
    this.selectedId.set(id);
    this.write(id);
  }

  reset(): void {
    this.years.set([]);
    this.selectedId.set(null);
    this.ready.set(false);
  }

  private storageKey(): string {
    return STORAGE_PREFIX + (this.auth.tenant()?.id ?? 'none');
  }

  private read(): string | null {
    try {
      return localStorage.getItem(this.storageKey());
    } catch {
      return null;
    }
  }

  private write(id: string): void {
    try {
      localStorage.setItem(this.storageKey(), id);
    } catch {
      // Without storage the choice simply lasts for this visit.
    }
  }
}

/** Adds the selected year to academy API calls. Sign-in, platform and year-list calls do not need it. */
export const academicYearInterceptor: HttpInterceptorFn = (request, next) => {
  const yearId = inject(AcademicYearService).selectedId();
  const url = request.url;
  const skip = !url.startsWith('/api/') || url.startsWith('/api/auth/') || url.startsWith('/api/platform') || url.startsWith('/api/academic-years');
  return yearId && !skip ? next(request.clone({ setHeaders: { [YEAR_HEADER]: yearId } })) : next(request);
};
