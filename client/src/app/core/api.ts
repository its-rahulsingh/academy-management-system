import { HttpClient, HttpErrorResponse, HttpInterceptorFn, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Observable, catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { Paged } from './models';

type QueryValue = string | number | boolean | null | undefined;

/** Thin wrapper over HttpClient: one place for the /api prefix and query-string building. */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  get<T>(path: string, query: Record<string, QueryValue> = {}): Observable<T> {
    return this.http.get<T>(`/api/${path}`, { params: toParams(query) });
  }

  post<T = unknown>(path: string, body: unknown = {}): Observable<T> {
    return this.http.post<T>(`/api/${path}`, body);
  }

  put<T = unknown>(path: string, body: unknown = {}): Observable<T> {
    return this.http.put<T>(`/api/${path}`, body);
  }

  delete<T = unknown>(path: string): Observable<T> {
    return this.http.delete<T>(`/api/${path}`);
  }
}

function toParams(query: Record<string, QueryValue>): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== null && value !== undefined && value !== '' && value !== 'All') params = params.set(key, String(value));
  }
  return params;
}

/** Builds a download link for a GET endpoint (CSV exports), using the same parameter rules as the API calls. */
export function apiUrl(path: string, query: Record<string, QueryValue> = {}): string {
  const text = toParams(query).toString();
  return `/api/${path}${text ? '?' + text : ''}`;
}

/** Turns any failed request into a sentence a user can act on. */
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'The server could not be reached. Check your connection and try again.';
    if (error.status === 429) return 'Too many attempts. Please wait a minute and try again.';
    if (error.status === 403) return error.error?.error ?? 'You do not have permission to do that.';
    return error.error?.error ?? 'The request failed. Please review the values and try again.';
  }
  return 'Something went wrong. Please try again.';
}

/**
 * Reacts to the two responses that change what the user may see:
 * 401 (session ended) sends them to sign-in; 402 (subscription lapsed) sends them to the subscription screen.
 */
export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      const isAuthCall = request.url.startsWith('/api/auth/');
      if (error.status === 401 && !isAuthCall) {
        auth.clear();
        void router.navigateByUrl('/login');
      } else if (error.status === 402) {
        void auth.refresh().then(() => router.navigateByUrl(auth.homeUrl()));
      }
      return throwError(() => error);
    }),
  );
};

// ---- Route guards ----

export const guestOnly: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.session().authenticated ? inject(Router).parseUrl(auth.homeUrl()) : true;
};

export const ownerOnly: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (!auth.session().authenticated) return inject(Router).parseUrl('/login');
  return auth.isOwner() ? true : inject(Router).parseUrl(auth.homeUrl());
};

export const academyOnly: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (!auth.session().authenticated) return inject(Router).parseUrl('/login');
  return auth.tenant() && !auth.isFaculty() && !auth.isStudent() ? true : inject(Router).parseUrl(auth.homeUrl());
};

/** The student portal: only a student login whose academy has access. */
export const studentOnly: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (!auth.session().authenticated) return inject(Router).parseUrl('/login');
  return auth.isStudent() && auth.hasAccess() ? true : inject(Router).parseUrl(auth.homeUrl());
};

/** The faculty portal: only a faculty login whose academy has access. */
export const facultyOnly: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (!auth.session().authenticated) return inject(Router).parseUrl('/login');
  return auth.isFaculty() && auth.hasAccess() ? true : inject(Router).parseUrl(auth.homeUrl());
};

/** For screens that need a subscription in good standing and, optionally, a permission. */
export function requires(permission?: string): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    if (!auth.hasAccess()) return inject(Router).parseUrl(auth.homeUrl());
    return !permission || auth.can(permission) ? true : inject(Router).parseUrl('/app/dashboard');
  };
}

// ---- Feedback ----

/** Short confirmation messages shown at the top of the workspace. */
@Injectable({ providedIn: 'root' })
export class Notify {
  readonly message = signal('');
  private timer?: ReturnType<typeof setTimeout>;

  success(message: string): void {
    this.message.set(message);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.message.set(''), 5000);
  }

  clear(): void {
    this.message.set('');
  }
}

/**
 * State for a server-paged table: current page, rows, total and loading/error flags.
 * Screens supply only the function that fetches a page.
 */
export class PagedList<T> {
  readonly items = signal<T[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly pageSize = 25;

  constructor(private readonly fetch: (page: number, pageSize: number) => Observable<Paged<T>>) {}

  load(page = this.page()): void {
    this.loading.set(true);
    this.error.set('');
    this.fetch(page, this.pageSize).subscribe({
      next: result => {
        this.items.set(result.items);
        this.total.set(result.total);
        this.page.set(result.page);
        this.loading.set(false);
      },
      error: error => {
        this.error.set(errorMessage(error));
        this.loading.set(false);
      },
    });
  }

  /** Call when a filter changes: results start again from the first page. */
  reload(): void {
    this.load(1);
  }
}

/**
 * State for a dialog that saves something: open flag, in-flight flag and the error to show inside it.
 * `submit` runs the request and closes the dialog on success.
 */
export class FormState {
  readonly open = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');

  show(): void {
    this.error.set('');
    this.busy.set(false);
    this.open.set(true);
  }

  close(): void {
    this.open.set(false);
    this.error.set('');
  }

  fail(message: string): void {
    this.error.set(message);
  }

  submit<T>(request: Observable<T>, onSaved: (result: T) => void): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    request.subscribe({
      next: result => {
        this.busy.set(false);
        this.close();
        onSaved(result);
      },
      error: error => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      },
    });
  }
}
