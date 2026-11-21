/** Shapes returned by the API. Kept in one place so a contract change is a single edit. */

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export type Role = 'Owner' | 'SuperAdmin' | 'Admin' | 'Accountant' | 'Staff' | 'Faculty' | 'Student';
export type LedgerStatus = 'Posted' | 'Voided';

export interface Session {
  authenticated: boolean;
  user: { id: string; name: string; email: string; role: Role } | null;
  permissions: string[];
  tenant: { id: string; code: string; name: string; tagline: string; logoUrl: string | null } | null;
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
  YearsManage: 'years.manage',
  BrandingManage: 'branding.manage',
  AdmissionsManage: 'admissions.manage',
  /** The staff workspace. Every academy role has it except a faculty portal login. */
  Workspace: 'workspace.use',
  FacultyPortal: 'faculty.portal',
  /** The day-by-day activity report (Super Admin). */
  ReportsActivity: 'reports.activity',
  /** Show the check-in QR code, see classes running now and stop one. */
  AttendanceManage: 'attendance.manage',
  StudentPortal: 'student.portal',
} as const;

// ---- Branding and admissions ----

export interface Branding {
  id: string;
  code: string;
  name: string;
  tagline: string;
  logoUrl: string | null;
  /** The name the academy's emails show as sender; empty uses the academy name. */
  emailSenderName: string;
  /** Where replies to the academy's emails go. */
  replyToEmail: string;
}

export interface PublicAcademy {
  name: string;
  tagline: string;
  logoUrl: string | null;
}

export interface AdmissionForm {
  academy: PublicAcademy;
  open: boolean;
  courses: { id: string; name: string; fee: number }[];
}

export type AdmissionStatus = 'Pending' | 'Approved' | 'Rejected';

export interface AdmissionApplication {
  id: string;
  fullName: string;
  email: string;
  phone: string;
  courseId: string;
  courseName: string;
  courseFee: number;
  courseActive: boolean;
  guardianName: string;
  address: string;
  message: string;
  status: AdmissionStatus;
  studentId: string | null;
  reviewNote: string;
  reviewedAtUtc: string | null;
  createdAtUtc: string;
}

export interface AdmissionQueue {
  counts: Record<AdmissionStatus, number>;
  page: Paged<AdmissionApplication>;
}

export interface AdmissionSettings {
  code: string;
  open: boolean;
  path: string;
}

export const PAYMENT_METHODS = ['Cash', 'Bank Transfer', 'UPI', 'Card', 'Cheque'] as const;

// ---- Academy ----

export interface AcademicYear {
  id: string;
  name: string;
  startDate: string;
  endDate: string;
  students: number;
  isCurrent: boolean;
  isClosed: boolean;
}

export interface AcademicYearList {
  currentId: string | null;
  canManage: boolean;
  items: AcademicYear[];
}

/** A subject as it appears linked to a course or a faculty member. */
export interface SubjectRef {
  id: string;
  name: string;
  code: string;
  active: boolean;
}

/** A row of the subject master, with how widely it is used. */
export interface Subject extends SubjectRef {
  courses: number;
  faculty: number;
  tests: number;
}

export interface Course {
  id: string;
  name: string;
  fee: number;
  active: boolean;
  subjects: SubjectRef[];
  enrolled: number;
  collected: number;
}

export interface Student {
  id: string;
  academicYearId: string;
  academicYearName: string;
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
  academicYearName?: string;
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
  /** The student's portal login, if they have one. */
  login: { email: string; active: boolean } | null;
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

export type FacultyPayType = 'Package' | 'Hourly';

export interface Faculty {
  id: string;
  fullName: string;
  email: string;
  phone: string;
  specialization: string;
  payType: FacultyPayType;
  /** Package amount; meaningful for package faculty. */
  agreedPayout: number;
  /** Rate per hour; meaningful for hourly faculty. */
  hourlyRate: number;
  active: boolean;
  subjects: SubjectRef[];
  minutesTaught: number;
  /** Package amount, or the total of logged sessions for hourly faculty. */
  earned: number;
  paid: number;
  /** Sessions the faculty member submitted from the portal that are waiting for approval. */
  pendingSessions: number;
  /** The faculty member's portal login, if they have one. */
  login: { email: string; active: boolean } | null;
}

export type SessionReview = 'Approved' | 'Pending' | 'Rejected';

export interface FacultySession {
  id: string;
  facultyId: string;
  facultyName: string;
  taughtOn: string;
  minutes: number;
  hourlyRate: number;
  amount: number;
  subjectId: string | null;
  subjectName: string | null;
  topic: string;
  status: LedgerStatus;
  voidReason: string | null;
  /** "HH:mm:ss" when the faculty member entered in and out times. */
  startTime: string | null;
  endTime: string | null;
  review: SessionReview;
  reviewNote: string;
  /** How the faculty member proved they were there, when the session came from clocking in and out. */
  clock: { inMethod: ClockMethod; outMethod: ClockMethod | null; inDistanceMeters: number | null; outDistanceMeters: number | null } | null;
}

// ---- Clocking in and out ----

export type ClockMethod = 'Qr' | 'Location' | 'Staff' | 'Manual' | 'Auto';

export interface ClockStatus {
  serverNowUtc: string;
  running: { id: string; inAtUtc: string; inMethod: ClockMethod; subjectName: string | null } | null;
  /** Classes left running past midnight and closed by the system; unpaid until an end time is entered. */
  needsEndTime: { id: string; inAtUtc: string; subjectName: string | null }[];
  locationAvailable: boolean;
  radiusMeters: number;
  maxClassMinutes: number;
  canClockIn: boolean;
}

export interface ClockResult {
  cancelled: boolean;
  minutes: number;
  amount: number;
  review: SessionReview | null;
}

export interface LiveClass {
  id: string;
  facultyId: string;
  facultyName: string;
  subjectName: string | null;
  inAtUtc: string;
  inMethod: ClockMethod;
  inDistanceMeters: number | null;
  verifiedBy: string | null;
  overdue: boolean;
  reminded: boolean;
}

export interface LiveClasses {
  serverNowUtc: string;
  maxClassMinutes: number;
  items: LiveClass[];
  needsEndTime: { id: string; facultyId: string; facultyName: string; subjectName: string | null; inAtUtc: string; inMethod: ClockMethod }[];
}

export interface CheckInQr {
  code: string;
  url: string;
  issuedAtUtc: string;
  expiresInSeconds: number;
  refreshSeconds: number;
}

export interface AttendanceSettings {
  latitude: number | null;
  longitude: number | null;
  radiusMeters: number;
  enabled: boolean;
  maxClassMinutes: number;
}

// ---- Student portal ----

export interface StudentPortalOverview {
  student: {
    id: string; fullName: string; email: string; phone: string; courseName: string; academicYearName: string; admissionDate: string; active: boolean;
  };
  fees: { feeAmount: number; discounts: number; surcharges: number; payable: number; paid: number; pending: number; status: string };
  subjects: { name: string; faculty: string[] }[];
  payments: { id: string; kind: 'Payment' | 'Refund'; receipt: string; amount: number; method: string; paidOn: string }[];
  adjustments: { kind: 'Discount' | 'Surcharge'; amount: number; reason: string; createdAtUtc: string }[];
  recentTests: { title: string; subject: string; testDate: string; marks: number; maxMarks: number; percent: number }[];
}

// ---- Faculty portal ----

/** How a session looks to the faculty member who taught it. */
export type PortalSessionState = 'Waiting' | 'Approved' | 'Paid' | 'Rejected' | 'Withdrawn';

export interface PortalSession {
  id: string;
  taughtOn: string;
  startTime: string | null;
  endTime: string | null;
  minutes: number;
  hourlyRate: number;
  amount: number;
  subjectName: string | null;
  topic: string;
  state: PortalSessionState;
  note: string | null;
  loggedByStaff: boolean;
}

export interface PortalOverview {
  faculty: { id: string; fullName: string; payType: FacultyPayType; hourlyRate: number; agreedPayout: number; active: boolean };
  month: string;
  thisMonth: { minutes: number; sessions: number; earned: number; waitingMinutes: number; waitingSessions: number };
  totals: {
    earned: number;
    paid: number;
    owed: number;
    unpaidSessions: number;
    waitingSessions: number;
    waitingAmount: number;
    lastPayout: { amount: number; paidOn: string; method: string } | null;
  };
  subjects: { id: string; name: string; active: boolean; courses: string[]; minutesThisMonth: number }[];
}

export interface PortalSessionList {
  month: string;
  counts: { all: number; waiting: number; approved: number; paid: number; removed: number };
  items: PortalSession[];
}

export interface PortalPayments {
  faculty: { fullName: string; payType: FacultyPayType; hourlyRate: number; agreedPayout: number };
  unpaid: number;
  unpaidSessions: number;
  received: number;
  paidSessions: number;
  waitingAmount: number;
  waitingSessions: number;
  lastPayout: { id: string; amount: number; paidOn: string; method: string; notes: string } | null;
  unpaidItems: PortalSession[];
  paidItems: PortalSession[];
  payouts: { id: string; amount: number; paidOn: string; method: string; notes: string }[];
  rates: { hourlyRate: number; from: string; to: string; sessions: number }[];
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
  subjectId: string;
  subjectName: string;
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

export interface StudentReportCard {
  academyName: string | null;
  student: { id: string; fullName: string; phone: string; email: string; courseName: string; academicYear: string; admissionDate: string; active: boolean };
  summary: {
    testsTaken: number;
    averagePercent: number | null;
    best: { title: string; percent: number } | null;
    lowest: { title: string; percent: number } | null;
  };
  subjects: { subject: string; tests: number; averagePercent: number }[];
  tests: {
    testId: string;
    title: string;
    subject: string;
    testDate: string;
    maxMarks: number;
    marks: number;
    percent: number;
    classAveragePercent: number;
    rank: number;
    takers: number;
    remarks: string;
  }[];
}

export interface Dashboard {
  counts: { totalStudents: number; activeStudents: number; totalCourses: number; totalFaculty: number };
  finance: {
    totalRevenue: number;
    pendingFees: number;
    earlierYearsPending: number;
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
  groupBy: ReportGrouping;
  /** "Day by day", "Week by week" or "Month by month". */
  title: string;
  admissions: number;
  periods: {
    label: string;
    from: string;
    to: string;
    admissions: number;
    collections: number;
    refunds: number;
    collectionsNet: number;
    expenses: number;
    payouts: number;
    net: number;
  }[];
  courses: { courseName: string; students: number; payable: number; collected: number; pending: number }[];
}

export type ReportGrouping = 'Day' | 'Week' | 'Month';

export interface ActivityTotals {
  admissions: number;
  applications: number;
  collections: number;
  refunds: number;
  discounts: number;
  surcharges: number;
  expenses: number;
  payouts: number;
  teachingMinutes: number;
  teachingPay: number;
  tests: number;
  netCash: number;
}

export interface ActivityEvent {
  date: string;
  /** Indian wall-clock time the entry was made, no zone. */
  enteredAt: string;
  type: string;
  title: string;
  detail: string;
  amount: number | null;
  /** "In", "Out" or "" for entries that are not cash. */
  flow: string;
  status: string;
  by: string;
}

export interface ActivityReport {
  from: string;
  to: string;
  groupBy: ReportGrouping;
  title: string;
  totals: ActivityTotals;
  periods: { label: string; from: string; to: string; totals: ActivityTotals }[];
  events: ActivityEvent[];
  eventCount: number;
  truncated: boolean;
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

export interface EmailStatus {
  configured: boolean;
  host: string | null;
  fromAddress: string | null;
  pending: number;
  sent: number;
  failed: number;
  recentProblems: { toAddress: string; subject: string; category: string; status: string; attempts: number; lastError: string; createdAtUtc: string }[];
}
