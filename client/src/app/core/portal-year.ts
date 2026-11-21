import { Injectable, signal } from '@angular/core';

export interface PortalYear {
  id: string;
  name: string;
  /** "yyyy-MM-dd"; given for faculty, whose month view stays inside the chosen year. */
  startDate?: string;
  endDate?: string;
}

/**
 * The academic year chosen in a faculty or student portal's top bar. Empty means "the current one":
 * a student's newest year, or for faculty the year containing today.
 */
@Injectable({ providedIn: 'root' })
export class PortalYearService {
  readonly years = signal<PortalYear[]>([]);
  readonly selected = signal('');

  reset(): void {
    this.years.set([]);
    this.selected.set('');
  }
}
