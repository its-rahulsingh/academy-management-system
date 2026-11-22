/** Turns raw activity-log rows into words people read: record names, field names, amounts and actions. */

const ENTITY_LABELS: Record<string, string> = {
  Student: 'Student',
  StudentPayment: 'Payment',
  FeeAdjustment: 'Fee adjustment',
  FacultyPayout: 'Faculty payout',
  FacultySession: 'Faculty class',
  FacultyClock: 'Clock in/out',
  AcademyTest: 'Test',
  TestResult: 'Test result',
  AppUser: 'User',
  SubscriptionInvoice: 'Invoice',
  Tenant: 'Academy settings',
  Expense: 'Expense',
  ExpenseProof: 'Expense bill',
  AdmissionApplication: 'Admission application',
  AcademicYear: 'Academic year',
  CourseSubject: 'Course subject',
  FacultySubject: 'Faculty subject',
};

export function entityLabel(entityType: string): string {
  return ENTITY_LABELS[entityType] ?? entityType.replace(/([a-z])([A-Z])/g, '$1 $2');
}

const ACTION_LABELS: Record<string, string> = {
  Created: 'Created',
  Updated: 'Changed',
  Deleted: 'Deleted',
  Voided: 'Cancelled',
  Login: 'Signed in',
  LoginFailed: 'Wrong password',
};

export function actionLabel(action: string): string {
  return ACTION_LABELS[action] ?? action;
}

const FIELD_LABELS: Record<string, string> = {
  FeeAmount: 'Agreed fee',
  FullName: 'Name',
  DisplayName: 'Name',
  VoidReason: 'Reason',
  PaidOn: 'Date',
  SpentOn: 'Date',
  TaughtOn: 'Date',
  ReceiptNumber: 'Receipt',
  SizeBytes: 'Size',
  MustChangePassword: 'Must change password',
  PasswordHash: 'Password',
  ReviewNote: 'Review note',
};

export function fieldLabel(field: string): string {
  if (FIELD_LABELS[field]) return FIELD_LABELS[field];
  const words = field.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

const MONEY_FIELDS = /^(Amount|FeeAmount|HourlyRate|AgreedPayout|Price|Total|LargeAmountAlert|ExpenseProofRequiredAbove)$/;
const INR = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 2 });

function show(field: string, value: unknown): string {
  if (value === null || value === undefined || value === '') return '—';
  if (MONEY_FIELDS.test(field) && !isNaN(Number(value))) return INR.format(Number(value));
  if (field === 'ReceiptNumber') return `RCP-${String(value).padStart(6, '0')}`;
  if (field === 'SizeBytes') return `${Math.max(1, Math.round(Number(value) / 1024))} KB`;
  if (field === 'Minutes') return `${value} min`;
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  return String(value);
}

export interface ChangeLine {
  field: string;
  text: string;
}

/** "Agreed fee: ₹45,000 → ₹40,000" lines, hiding internal ids, timestamps and empty values. */
export function changeLines(changes: string | null, max = 6): ChangeLine[] {
  if (!changes) return [];
  let parsed: Record<string, unknown>;
  try {
    parsed = JSON.parse(changes);
  } catch {
    return [];
  }
  return Object.entries(parsed)
    .filter(([field]) => !/Id$|AtUtc$|^Data$/.test(field))
    .filter(([, value]) => value !== null && value !== '')
    .slice(0, max)
    .map(([field, value]) => {
      if (value && typeof value === 'object' && 'new' in value) {
        const change = value as { old: unknown; new: unknown };
        return { field: fieldLabel(field), text: `${show(field, change.old)} → ${show(field, change.new)}` };
      }
      return { field: fieldLabel(field), text: show(field, value) };
    });
}

export function logTime(value: string): string {
  return new Date(value).toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}
