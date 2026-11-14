import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Public marketing page. Figures in the preview are illustrative and say so. */
@Component({
  selector: 'app-landing',
  imports: [RouterLink],
  template: `
    <div class="marketing">
      <header class="marketing-header">
        <div class="logo-line"><span class="brand-mark">A</span><strong>AcademyFlow</strong></div>
        <nav>
          <a routerLink="/" fragment="features">Features</a>
          <a routerLink="/" fragment="platform">Platform</a>
          <a routerLink="/" fragment="pricing">Pricing</a>
        </nav>
        <div><a class="primary" routerLink="/login">Sign in →</a></div>
      </header>

      <section class="marketing-hero">
        <span class="marketing-eyebrow">FINANCIAL CLARITY FOR ACADEMIES</span>
        <h1>Run your academy<br />finances <span>with clarity.</span></h1>
        <p>
          Track student fees, faculty payouts and expenses from one workspace. Replace scattered spreadsheets with
          figures you can trust.
        </p>
        <a class="primary" routerLink="/login">Sign in to AcademyFlow →</a>
        <a class="secondary explore" routerLink="/" fragment="features">Explore the product</a>

        <div class="marketing-window">
          <div class="window-dots">● ● ● <span>AcademyFlow — dashboard preview</span></div>
          <h3>Financial overview</h3>
          <p>Academy performance at a glance</p>
          <div class="fake-metrics">
            <div><small>TOTAL REVENUE</small><b>₹18,42,500</b></div>
            <div><small>ACTIVE STUDENTS</small><b>342</b></div>
            <div><small>PENDING FEES</small><b>₹4,12,800</b></div>
            <div><small>FACULTY PAYABLE</small><b>₹2,85,000</b></div>
          </div>
          <div class="fake-chart">
            @for (height of previewBars; track $index) {
              <span [style.height.%]="height"></span>
            }
          </div>
          <div class="preview-disclaimer">Illustrative preview — not real academy data</div>
        </div>
      </section>

      <section class="marketing-section" id="features">
        <div class="marketing-eyebrow">ONE CONNECTED WORKSPACE</div>
        <h2>Everything you need to run academy finances</h2>
        <p>From fee collection to faculty payouts, with every rupee accounted for.</p>
        <div class="feature-grid">
          <article><b>▣</b><h3>Fees and receipts</h3><p>Installments, discounts, refunds and numbered receipts you can print.</p></article>
          <article><b>♧</b><h3>Faculty payouts</h3><p>Agreed amounts, what has been paid and what is still owed.</p></article>
          <article><b>⌁</b><h3>Reports</h3><p>Collections, expenses and cash balance by month, with CSV export.</p></article>
          <article><b>▤</b><h3>Courses and results</h3><p>Enrolments, tests and report cards alongside the fee position.</p></article>
        </div>
      </section>

      <section class="marketing-section marketing-tinted" id="platform">
        <div class="marketing-eyebrow">THE PLATFORM</div>
        <h2>Built for control</h2>
        <p>Each academy's data is private to that academy, and every change is recorded.</p>
        <div class="feature-grid">
          <article><h3>Your own workspace</h3><p>Each academy signs in with its own code and sees only its own records.</p></article>
          <article><h3>Roles that fit</h3><p>Super Admin, Admin, Accountant and Staff each see only what their job needs.</p></article>
          <article><h3>Corrections, not deletions</h3><p>Mistakes are voided with a reason, so the books always reconcile.</p></article>
          <article><h3>Audit trail</h3><p>Who recorded, changed or voided what — and when.</p></article>
        </div>
      </section>

      <section class="marketing-section" id="pricing">
        <div class="marketing-eyebrow">PRICING</div>
        <h2>Simple subscription pricing</h2>
        <p>Accounts are set up for you. Contact us to get your academy started.</p>
        <div class="price-grid">
          <article><small>Starter</small><h3>₹2,499 <span>/month</span></h3><p>For single-location academies getting organised.</p></article>
          <article class="popular"><small>Growth</small><h3>₹6,999 <span>/month</span></h3><p>For growing academies that need complete visibility.</p></article>
          <article><small>Enterprise</small><h3>Custom</h3><p>For larger academy networks.</p></article>
        </div>
      </section>

      <footer>AcademyFlow · Your academy, one source of truth.</footer>
    </div>
  `,
})
export class LandingComponent {
  protected readonly previewBars = [35, 60, 44, 73, 56, 88, 64, 95, 74, 80];
}
