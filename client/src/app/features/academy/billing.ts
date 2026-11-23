import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api, Notify, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { CONTACT, whatsappLink } from '../../core/contact';
import { BillingOverview, CheckoutOrder, Invoice, Permission, PlanOffer } from '../../core/models';
import { UI } from '../../shared/ui';

const CHECKOUT_SCRIPT = 'https://checkout.razorpay.com/v1/checkout.js';

/** The parts of Razorpay's browser checkout this page uses. */
interface RazorpayResult {
  razorpay_order_id: string;
  razorpay_payment_id: string;
  razorpay_signature: string;
}
interface RazorpayOptions {
  key: string;
  amount: number;
  currency: string;
  order_id: string;
  name: string;
  description: string;
  handler: (result: RazorpayResult) => void;
  modal: { ondismiss: () => void };
}
declare global {
  interface Window {
    Razorpay?: new (options: RazorpayOptions) => { open(): void };
  }
}

/** The academy's own subscription: plan, period, invoices, and paying them. Reachable even when access has lapsed. */
@Component({
  selector: 'app-billing',
  imports: [UI],
  template: `
    <div class="page-header">
      <div>
        <div class="page-kicker">ACADEMY WORKSPACE</div>
        <h1>Subscription</h1>
        <p>Your Ojas plan and invoices.</p>
      </div>
    </div>

    @if (!auth.hasAccess()) {
      <div class="alert" role="alert">{{ auth.access()?.reason ?? 'This academy does not have an active subscription.' }}</div>
    }
    @if (error()) {
      <div class="alert" role="alert">{{ error() }}</div>
    }

    @if (!canManage) {
      <section class="surface empty">Only a Super Admin can view and pay the subscription.</section>
    } @else if (data(); as d) {
      @if (d.subscription; as s) {
        <div class="stats four">
          <article class="stat purple">
            <div class="stat-top">PLAN</div><div class="stat-value small-value">{{ s.planName }}</div>
            <small>{{ s.price > 0 ? (s.price | money) + ' / ' + s.interval.toLowerCase() : 'Free forever' }}</small>
          </article>
          <article class="stat white"><div class="stat-top">STATUS</div><div class="stat-value small-value">{{ s.price > 0 ? (auth.access()?.status ?? s.status) : 'Active' }}</div></article>
          <article class="stat white">
            <div class="stat-top">{{ s.price <= 0 ? 'RENEWAL' : s.status === 'Trialing' ? 'TRIAL ENDS' : 'PAID THROUGH' }}</div>
            <div class="stat-value small-value">{{ s.price <= 0 ? 'Never expires' : (s.currentPeriodEnd | day) }}</div>
            @if (s.status === 'Trialing') { <small>Then the Free plan, unless you upgrade</small> }
          </article>
          <article class="stat white">
            <div class="stat-top">YOUR LOGO</div>
            <div class="stat-value small-value">{{ s.customBranding ? 'Shown' : 'Not included' }}</div>
            <small>{{ s.customBranding ? 'On every screen and form' : '"Powered by Ojas" is shown' }}</small>
          </article>
        </div>

        <section class="surface">
          <div class="surface-title"><h2>Plan limits</h2></div>
          <p class="muted">Limits only stop you adding more. Nothing you already have is ever removed.</p>
          <div class="limit-grid">
            @for (l of limits(d); track l.label) {
              <div class="limit" [class.full]="l.max !== null && l.used >= l.max">
                <div class="limit-head"><span>{{ l.label }}</span><b>{{ l.used }}{{ l.max === null ? '' : ' / ' + l.max }}</b></div>
                <div class="limit-bar"><i [style.width.%]="l.max === null ? 8 : (l.max === 0 ? 100 : Math.min(100, (l.used / l.max) * 100))"></i></div>
                <small>{{ l.max === null ? 'Unlimited' : l.used >= l.max ? 'Limit reached — upgrade to add more' : (l.max - l.used) + ' left' }}</small>
              </div>
            }
          </div>
        </section>

        <section class="surface">
          <div class="surface-title"><h2>Plans</h2></div>
          <p class="muted">To change plan, message us on WhatsApp ({{ contact.phone }}) or email {{ contact.email }}. We switch it the same day.</p>
          <div class="plan-grid">
            @for (p of monthlyPlans(d); track p.code) {
              <article class="plan-card" [class.current]="p.code === s.planCode">
                <h3>{{ p.name }}</h3>
                <div class="plan-price">{{ p.price > 0 ? (p.price | money) : '₹0' }}<small>{{ p.price > 0 ? ' / month' : ' forever' }}</small></div>
                @if (yearly(d, p); as y) { <small class="muted">or {{ y.price | money }} a year</small> }
                <ul>
                  <li>{{ p.maxStudents ?? 'Unlimited' }} students</li>
                  <li>{{ p.maxFaculty ?? 'Unlimited' }} faculty</li>
                  <li>{{ p.maxUsers ?? 'Unlimited' }} staff {{ p.maxUsers === 1 ? 'login' : 'logins' }}</li>
                  <li>{{ p.maxReceiptEmailsPerMonth === null ? 'Unlimited' : p.maxReceiptEmailsPerMonth }} emailed receipts a month</li>
                  <li>{{ p.customBranding ? 'Your own logo' : '"Powered by Ojas" shown' }}</li>
                </ul>
                @if (p.code === s.planCode) {
                  <span class="current-tag">Your plan</span>
                } @else if (p.price > s.price) {
                  <a class="primary" [href]="upgradeLink(p)" target="_blank" rel="noopener">Upgrade to {{ p.name }}</a>
                }
              </article>
            }
          </div>
        </section>
      } @else {
        <section class="surface empty">No plan has been assigned yet. Please contact the platform administrator.</section>
      }

      <section class="surface">
        <div class="surface-title"><h2>Invoices</h2></div>
        @if (!d.onlinePayment.enabled) {
          <p class="muted">Online payment is not switched on. Pay by bank transfer or UPI and the platform administrator will mark the invoice as paid.</p>
        }
        <div class="table-scroll">
          <table>
            <thead><tr><th>INVOICE</th><th>PERIOD</th><th>DUE</th><th>AMOUNT</th><th>STATUS</th><th></th></tr></thead>
            <tbody>
              @for (invoice of d.invoices; track invoice.id) {
                <tr>
                  <td><strong>{{ invoice.invoiceNumber }}</strong><small>{{ invoice.planName }}</small></td>
                  <td>{{ invoice.periodStart | day }} – {{ invoice.periodEnd | day }}</td>
                  <td>{{ invoice.dueOn | day }}</td>
                  <td>{{ invoice.amount | money: true }}</td>
                  <td><app-status [value]="invoice.status" /></td>
                  <td class="row-actions">
                    @if (invoice.status === 'Open' && d.onlinePayment.enabled) {
                      <button type="button" class="primary" [disabled]="paying()" (click)="pay(invoice)">{{ paying() === invoice.id ? 'Opening…' : 'Pay now' }}</button>
                    } @else if (invoice.status === 'Paid') {
                      <small>{{ invoice.paymentMethod }} · {{ invoice.paidAtUtc | day }}</small>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="6" class="empty">No invoices yet.</td></tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    }
  `,
  styles: `
    .limit-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 14px; margin-top: 6px; }
    .limit { border: 1px solid #eceef5; border-radius: 12px; padding: 14px; }
    .limit-head { display: flex; justify-content: space-between; gap: 8px; font-size: 12px; color: #6b7088; font-weight: 700; }
    .limit-head b { color: #1e2438; font-size: 14px; }
    .limit-bar { height: 8px; border-radius: 4px; background: #efedff; margin: 10px 0 8px; overflow: hidden; }
    .limit-bar i { display: block; height: 100%; border-radius: 4px; background: #5147df; }
    .limit small { font-size: 11px; color: #8d91a3; }
    .limit.full .limit-bar i { background: #e5484d; } .limit.full small { color: #e5484d; font-weight: 700; }
    .plan-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 14px; margin-top: 6px; }
    .plan-card { display: flex; flex-direction: column; gap: 6px; border: 1px solid #eceef5; border-radius: 14px; padding: 18px; }
    .plan-card.current { border-color: #5147df; box-shadow: 0 0 0 3px #5147df1a; }
    .plan-card h3 { margin: 0; font-size: 16px; }
    .plan-price { font-size: 24px; font-weight: 800; letter-spacing: -.03em; } .plan-price small { font-size: 12px; color: #8d91a3; font-weight: 600; }
    .plan-card ul { list-style: none; padding: 0; margin: 8px 0; flex: 1; font-size: 12px; color: #4b5068; display: grid; gap: 6px; }
    .plan-card li::before { content: '✓'; color: #1fa971; font-weight: 800; margin-right: 6px; }
    .plan-card a.primary { text-align: center; text-decoration: none; }
    .current-tag { text-align: center; font-size: 12px; font-weight: 800; color: #5147df; background: #efedff; border-radius: 8px; padding: 10px; }
  `,
})
export class BillingComponent {
  protected readonly Math = Math;
  protected readonly contact = CONTACT;
  private readonly api = inject(Api);
  private readonly notify = inject(Notify);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);
  protected readonly canManage = this.auth.can(Permission.BillingManage);

  protected readonly data = signal<BillingOverview | null>(null);
  protected readonly error = signal('');
  /** Id of the invoice whose checkout is opening, so its button can show progress. */
  protected readonly paying = signal<string | null>(null);

  constructor() {
    if (this.canManage) this.load();
  }

  private load(): void {
    this.api.get<BillingOverview>('billing').subscribe({ next: d => this.data.set(d), error: e => this.error.set(errorMessage(e)) });
  }

  /** Each plan limit next to what the academy uses now (the same counts the server checks). */
  protected limits(d: BillingOverview): { label: string; used: number; max: number | null }[] {
    const s = d.subscription!;
    return [
      { label: 'Active students', used: d.usage.students, max: s.maxStudents },
      { label: 'Active faculty', used: d.usage.faculty, max: s.maxFaculty },
      { label: 'Staff logins', used: d.usage.users, max: s.maxUsers },
      { label: 'Receipts emailed this month', used: d.usage.receiptEmails, max: s.maxReceiptEmailsPerMonth },
    ];
  }

  /** The plans to compare: monthly prices, with the yearly option shown under its monthly twin. */
  protected monthlyPlans(d: BillingOverview): PlanOffer[] {
    return d.plans.filter(p => p.interval === 'Monthly').sort((a, b) => a.price - b.price);
  }

  protected yearly(d: BillingOverview, monthly: PlanOffer): PlanOffer | undefined {
    return d.plans.find(p => p.interval === 'Yearly' && p.code === `${monthly.code}-yearly`);
  }

  protected upgradeLink(plan: PlanOffer): string {
    const academy = this.auth.tenant();
    return whatsappLink(`Hi, I would like to upgrade ${academy?.name ?? 'my academy'} (code ${academy?.code ?? ''}) to the Ojas ${plan.name} plan.`);
  }

  protected async pay(invoice: Invoice): Promise<void> {
    this.paying.set(invoice.id);
    this.error.set('');
    try {
      await this.loadCheckoutScript();
      this.api.post<CheckoutOrder>(`billing/invoices/${invoice.id}/checkout`).subscribe({
        next: order => this.openCheckout(invoice, order),
        error: e => this.fail(errorMessage(e)),
      });
    } catch {
      this.fail('The payment window could not be loaded. Check your connection and try again.');
    }
  }

  private openCheckout(invoice: Invoice, order: CheckoutOrder): void {
    if (!window.Razorpay) return this.fail('The payment window could not be loaded.');

    new window.Razorpay({
      key: order.keyId,
      amount: order.amount,
      currency: order.currency,
      order_id: order.orderId,
      name: 'Ojas',
      description: `Invoice ${order.invoiceNumber}`,
      // The browser's word is not trusted: the server re-checks the signature before marking the invoice paid.
      handler: result => this.confirm(invoice, result),
      modal: { ondismiss: () => this.paying.set(null) },
    }).open();
  }

  private confirm(invoice: Invoice, result: RazorpayResult): void {
    const body = { orderId: result.razorpay_order_id, paymentId: result.razorpay_payment_id, signature: result.razorpay_signature };
    this.api.post(`billing/invoices/${invoice.id}/confirm`, body).subscribe({
      next: async () => {
        this.paying.set(null);
        this.notify.success(`Payment received for ${invoice.invoiceNumber}`);
        const hadAccess = this.auth.hasAccess();
        await this.auth.refresh();
        this.load();
        if (!hadAccess && this.auth.hasAccess()) await this.router.navigateByUrl('/app/dashboard');
      },
      error: e => this.fail(errorMessage(e)),
    });
  }

  private fail(message: string): void {
    this.paying.set(null);
    this.error.set(message);
  }

  private loadCheckoutScript(): Promise<void> {
    if (window.Razorpay) return Promise.resolve();
    return new Promise((resolve, reject) => {
      const script = document.createElement('script');
      script.src = CHECKOUT_SCRIPT;
      script.onload = () => resolve();
      script.onerror = () => reject(new Error('checkout script failed to load'));
      document.head.appendChild(script);
    });
  }
}
