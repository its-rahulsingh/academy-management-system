/** Shapes returned by the API. Kept in one place so a contract change is a single edit. */

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export type Role = 'Owner' | 'SuperAdmin' | 'Admin' | 'Accountant' | 'Staff';
export type LedgerStatus = 'Posted' | 'Voided';

export interface Session {
  authenticated: boolean;
  user: { id: string; name: string; email: string; role: Role } | null;
  permissions: string[];
  tenant: { id: string; code: string; name: string } | null;
  access: {
    allowed: boolean;
    status: string;
    reason: string | null;
    planName: string | null;
    periodEnd: string | null;
    inGrace: boolean;
  } | null;
  mustChangePassword: boolean;
}

export const ANONYMOUS: Session = {
  authenticated: false,
  user: null,
  permissions: [],
  tenant: null,
  access: null,
  mustChangePassword: false,
};

/** Mirrors Permissions.cs on the server. The server is the authority; these only decide what to show. */
export const Permission = {
  DashboardFinance: 'dashboard.finance',
  StudentsRead: 'students.read',
  StudentsWrite: 'students.write',
  CoursesRead: 'courses.read',
  CoursesWrite: 'courses.write',
  FacultyRead: 'faculty.read',
  FacultyWrite: 'faculty.write',
  PaymentsRead: 'payments.read',
  PaymentsWrite: 'payments.write',
  LedgerCorrect: 'ledger.correct',
  PayoutsRead: 'payouts.read',
  PayoutsWrite: 'payouts.write',
  ExpensesRead: 'expenses.read',
  ExpensesWrite: 'expenses.write',
  TestsRead: 'tests.read',
  TestsWrite: 'tests.write',
  ReportsRead: 'reports.read',
  RemindersSend: 'reminders.send',
  UsersManage: 'users.manage',
  AuditRead: 'audit.read',
  BillingManage: 'billing.manage',
} as const;

export const PAYMENT_METHODS = ['Cash', 'Bank Transfer', 'UPI', 'Card', 'Cheque'] as const;

// ---- Academy ----

export interface Course {
  id: string;
  name: string;
  fee: number;
  active: boolean;
  enrolled: number;
  collected: number;
}

export interface Student {
  id: string;
  fullName: string;
  email: string;
  phone: string;
  courseId: string;
  courseName: string;
  feeAmount: number;
  admissionDate: string;
  remarks: string;
  active: boolean;
  discounts: number;
  surcharges: number;
  payable: number;
  paid: number;
  pending: number;
  status: 'Paid' | 'Partial' | 'Pending';
}

export interface StudentLookup {
  id: string;
  fullName: string;
  courseName: string;
  pending: number;
  paid: number;
}

export interface Payment {
  id: string;
  studentId: string;
  studentName: string;
  kind: 'Payment' | 'Refund';
  receipt: string;
  amount: number;
  method: string;
  paidOn: string;
  notes: string;
  status: LedgerStatus;
  voidReason: string | null;
}

export interface FeeAdjustment {
  id: string;
  kind: 'Discount' | 'Surcharge';
  amount: number;
  reason: string;
  status: LedgerStatus;
  voidReason: string | null;
  createdAtUtc: string;
}

export interface StudentDetail {
  student: Student;
  payments: Omit<Payment, 'studentId' | 'studentName'>[];
  adjustments: FeeAdjustment[];
}

export interface Receipt {
  academyName: string;
  receipt: string;
  kind: 'Payment' | 'Refund';
  amount: number;
  method: string;
  paidOn: string;
  notes: string;
  status: LedgerStatus;
  voidReason: string | null;
  studentName: string;
  studentPhone: string;
  recordedBy: string | null;
  courseName: string;
  feePayable: number;
  totalPaid: number;
  balanceDue: number;
}

export interface PaymentSummary {
  received: number;
  refunded: number;
  net: number;
  thisMonth: number;
  count: number;
}

export interface Faculty {
  id: string;
  fullName: string;
  email: string;
  phone: string;
  specialization: string;
  agreedPayout: number;
  active: boolean;
  paid: number;
}

export interface Payout {
  id: string;
  facultyId: string;
  facultyName: string;
  amount: number;
  method: string;
  paidOn: string;
  notes: string;
  status: LedgerStatus;
  voidReason: string | null;
}

export interface Expense {
  id: string;
  title: string;
  amount: number;
  category: string;
  spentOn: string;
  method: string;
  notes: string;
  status: LedgerStatus;
  voidReason: string | null;
}

export interface ExpenseSummary {
  total: number;
  thisMonth: number;
  count: number;
  topCategory: string | null;
  categories: string[];
}

export interface AcademyTest {
  id: string;
  title: string;
  subject: string;
  courseId: string;
  courseName: string;
  maxMarks: number;
  testDate: string;
  notes: string;
  resultCount: number;
  averageMarks: number | null;
}

export interface TestResult {
  id: string;
  studentId: string;
  studentName: string;
  marks: number;
  remarks: string;
}

export interface ReportCard {
  studentId: string;
  fullName: string;
  courseName: string;
  testsTaken: number;
  averagePercent: number | null;
}

export interface Dashboard {
  counts: { totalStudents: number; activeStudents: number; totalCourses: number; totalFaculty: number };
  finance: {
    totalRevenue: number;
    pendingFees: number;
    facultyPayable: number;
    facultyPaidOut: number;
    totalExpenses: number;
    netCashBalance: number;
    recentPayments: { id: string; studentName: string; kind: string; amount: number; paidOn: string; method: string }[];
    coursePerformance: { courseName: string; students: number; collected: number; pending: number }[];
  } | null;
}

export interface ReportSummary {
  from: string;
  to: string;
  collections: number;
  refunds: number;
  netCollections: number;
  expenses: number;
  payouts: number;
  netCash: number;
  byMethod: { method: string; amount: number; count: number }[];
  expensesByCategory: { category: string; amount: number; count: number }[];
  months: { month: string; collections: number; expenses: number; payouts: number; net: number }[];
  courses: { courseName: string; students: number; payable: number; collected: number; pending: number }[];
}

export interface AppUser {
  id: string;
  email: string;
  displayName: string;
  role: Role;
  active: boolean;
  lastLoginAtUtc: string | null;
  canManage?: boolean;
}

export interface UserList {
  assignableRoles: Role[];
  items: AppUser[];
}

export interface AuditEntry {
  id: number;
  tenantId?: string | null;
  userName: string;
  action: string;
  entityType: string;
  entityId: string;
  changes: string | null;
  createdAtUtc: string;
}

// ---- Subscription ----

export type BillingInterval = 'Monthly' | 'Quarterly' | 'Yearly';
export type SubscriptionStatus = 'Trialing' | 'Active' | 'PastDue' | 'Cancelled';
export type InvoiceStatus = 'Open' | 'Paid' | 'Void';

export interface Invoice {
  id: string;
  invoiceNumber: string;
  tenantId?: string;
  tenantName?: string;
  planName: string;
  amount: number;
  currency: string;
  periodStart: string;
  periodEnd: string;
  dueOn: string;
  status: InvoiceStatus;
  paidAtUtc: string | null;
  paymentMethod: string | null;
  notes?: string;
}

export interface BillingOverview {
  subscription: {
    planName: string;
    description: string;
    maxStudents: number | null;
    maxUsers: number | null;
    status: SubscriptionStatus;
    price: number;
    currency: string;
    interval: BillingInterval;
    currentPeriodStart: string;
    currentPeriodEnd: string;
  } | null;
  invoices: Invoice[];
  usage: { students: number; users: number };
  onlinePayment: { enabled: boolean; provider: string; keyId: string | null };
}

export interface CheckoutOrder {
  orderId: string;
  keyId: string;
  amount: number;
  currency: string;
  invoiceNumber: string;
}

// ---- Platform owner ----

export interface Plan {
  id: string;
  code: string;
  name: string;
  description: string;
  price: number;
  currency: string;
  interval: BillingInterval;
  maxStudents: number | null;
  maxUsers: number | null;
  isActive: boolean;
  subscribers: number;
}

export interface TenantRow {
  id: string;
  code: string;
  name: string;
  status: 'Active' | 'Suspended';
  contactName: string;
  contactEmail: string;
  createdAtUtc: string;
  subscription: {
    status: SubscriptionStatus;
    currentPeriodEnd: string;
    planName: string;
    price: number;
    currency: string;
    interval: BillingInterval;
  } | null;
  students: number;
  users: number;
}

export interface TenantDetail {
  id: string;
  code: string;
  name: string;
  status: 'Active' | 'Suspended';
  contactName: string;
  contactEmail: string;
  contactPhone: string;
  createdAtUtc: string;
  suspendedAtUtc: string | null;
  suspensionReason: string | null;
  subscription: {
    id: string;
    planId: string;
    planName: string;
    status: SubscriptionStatus;
    price: number;
    currency: string;
    interval: BillingInterval;
    currentPeriodStart: string;
    currentPeriodEnd: string;
  } | null;
  users: AppUser[];
  invoices: Invoice[];
  usage: { students: number; courses: number; users: number };
}

export interface PlatformSummary {
  totalTenants: number;
  activeTenants: number;
  suspendedTenants: number;
  payingSubscriptions: number;
  monthlyRecurringRevenue: number;
  openInvoices: number;
  openInvoiceAmount: number;
  overdueInvoices: number;
  collectedThisMonth: number;
}
