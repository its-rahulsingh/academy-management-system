import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api, Notify, errorMessage } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { BillingOverview, CheckoutOrder, Invoice, Permission } from '../../core/models';
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
        <p>Your AcademyFlow plan and invoices.</p>
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
          <article class="stat purple"><div class="stat-top">PLAN</div><div class="stat-value small-value">{{ s.planName }}</div><small>{{ s.price | money }} / {{ s.interval.toLowerCase() }}</small></article>
          <article class="stat white"><div class="stat-top">STATUS</div><div class="stat-value small-value">{{ auth.access()?.status ?? s.status }}</div></article>
          <article class="stat white"><div class="stat-top">{{ s.status === 'Trialing' ? 'TRIAL ENDS' : 'PAID THROUGH' }}</div><div class="stat-value small-value">{{ s.currentPeriodEnd | day }}</div></article>
          <article class="stat white">
            <div class="stat-top">USAGE</div>
            <div class="stat-value small-value">{{ d.usage.students }}{{ s.maxStudents ? ' / ' + s.maxStudents : '' }} students</div>
            <small>{{ d.usage.users }}{{ s.maxUsers ? ' / ' + s.maxUsers : '' }} users</small>
          </article>
        </div>
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
})
export class BillingComponent {
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
      name: 'AcademyFlow',
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
