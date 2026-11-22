import { Routes } from '@angular/router';
import { academyOnly, facultyOnly, guestOnly, ownerOnly, requires, studentOnly } from './core/api';
import { Permission } from './core/models';

/**
 * Three areas: public pages, the academy workspace (/app) and the platform owner's console (/platform).
 * Each screen is loaded on demand, so the first page stays small as the product grows.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', canActivate: [guestOnly], loadComponent: () => import('./features/public/landing').then(m => m.LandingComponent) },
  { path: 'login', canActivate: [guestOnly], loadComponent: () => import('./features/public/auth-pages').then(m => m.LoginComponent) },
  { path: 'forgot-password', loadComponent: () => import('./features/public/auth-pages').then(m => m.ForgotPasswordComponent) },
  { path: 'reset-password', loadComponent: () => import('./features/public/auth-pages').then(m => m.ResetPasswordComponent) },
  { path: 'locked', loadComponent: () => import('./features/public/auth-pages').then(m => m.LockedComponent) },
  // Public admission form, reached by link or QR code. Open to everyone, signed in or not.
  { path: 'apply/:code', loadComponent: () => import('./features/public/apply').then(m => m.ApplyComponent) },
  {
    path: 'app',
    canActivate: [academyOnly],
    loadComponent: () => import('./features/shell/shell').then(m => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', canActivate: [requires()], loadComponent: () => import('./features/academy/dashboard').then(m => m.DashboardComponent) },
      { path: 'students', canActivate: [requires(Permission.StudentsRead)], loadComponent: () => import('./features/academy/students').then(m => m.StudentsComponent) },
      { path: 'students/promote', canActivate: [requires(Permission.StudentsWrite)], loadComponent: () => import('./features/academy/promote').then(m => m.PromoteComponent) },
      { path: 'admissions', canActivate: [requires(Permission.AdmissionsManage)], loadComponent: () => import('./features/academy/admissions').then(m => m.AdmissionsComponent) },
      { path: 'profile', canActivate: [requires(Permission.BrandingManage)], loadComponent: () => import('./features/academy/branding').then(m => m.BrandingPageComponent) },
      { path: 'courses', canActivate: [requires(Permission.CoursesRead)], loadComponent: () => import('./features/academy/courses').then(m => m.CoursesComponent) },
      { path: 'subjects', canActivate: [requires(Permission.CoursesRead)], loadComponent: () => import('./features/academy/subjects').then(m => m.SubjectsComponent) },
      { path: 'faculty', canActivate: [requires(Permission.FacultyRead)], loadComponent: () => import('./features/academy/faculty').then(m => m.FacultyComponent) },
      { path: 'payments', canActivate: [requires(Permission.PaymentsRead)], loadComponent: () => import('./features/academy/payments').then(m => m.PaymentsComponent) },
      { path: 'payments/:id/receipt', canActivate: [requires(Permission.PaymentsRead)], loadComponent: () => import('./features/academy/receipt').then(m => m.ReceiptComponent) },
      { path: 'tests/report-cards/:id', canActivate: [requires(Permission.TestsRead)], loadComponent: () => import('./features/academy/report-card').then(m => m.ReportCardComponent) },
      { path: 'expenses', canActivate: [requires(Permission.ExpensesRead)], loadComponent: () => import('./features/academy/expenses').then(m => m.ExpensesComponent) },
      { path: 'tests', canActivate: [requires(Permission.TestsRead)], loadComponent: () => import('./features/academy/tests').then(m => m.TestsComponent) },
      { path: 'reports', canActivate: [requires(Permission.ReportsRead)], loadComponent: () => import('./features/academy/reports').then(m => m.ReportsComponent) },
      { path: 'attendance', canActivate: [requires(Permission.AttendanceManage)], loadComponent: () => import('./features/academy/attendance').then(m => m.AttendanceComponent) },
      { path: 'daily-report', canActivate: [requires(Permission.ReportsActivity)], loadComponent: () => import('./features/academy/daily-report').then(m => m.DailyReportComponent) },
      { path: 'users', canActivate: [requires(Permission.UsersManage)], loadComponent: () => import('./features/academy/users').then(m => m.UsersComponent) },
      { path: 'audit', canActivate: [requires(Permission.AuditRead)], loadComponent: () => import('./features/academy/audit').then(m => m.AuditComponent) },
      { path: 'academic-years', canActivate: [requires()], loadComponent: () => import('./features/academy/academic-years').then(m => m.AcademicYearsComponent) },
      // No subscription check here: an expired academy must be able to reach this page to pay.
      { path: 'billing', loadComponent: () => import('./features/academy/billing').then(m => m.BillingComponent) },
    ],
  },
  {
    // A faculty member's own portal: their teaching time and pay, nothing else.
    path: 'teacher',
    canActivate: [facultyOnly],
    loadComponent: () => import('./features/teacher/teacher-shell').then(m => m.PortalShellComponent),
    data: { nav: 'teacher' },
    children: [
      { path: '', pathMatch: 'full', loadComponent: () => import('./features/teacher/teacher-dashboard').then(m => m.TeacherDashboardComponent) },
      { path: 'payments', loadComponent: () => import('./features/teacher/teacher-payments').then(m => m.TeacherPaymentsComponent) },
      // Opened from the admin's QR code with the phone's own camera: clocks in or out straight away.
      { path: 'scan', loadComponent: () => import('./features/teacher/teacher-scan').then(m => m.TeacherScanComponent) },
    ],
  },
  {
    // A student's own portal: their fees, receipts and test results.
    path: 'student',
    canActivate: [studentOnly],
    loadComponent: () => import('./features/teacher/teacher-shell').then(m => m.PortalShellComponent),
    data: { nav: 'student' },
    children: [
      { path: '', pathMatch: 'full', loadComponent: () => import('./features/student/student-dashboard').then(m => m.StudentDashboardComponent) },
      { path: 'report-card', data: { portal: true }, loadComponent: () => import('./features/academy/report-card').then(m => m.ReportCardComponent) },
    ],
  },
  {
    path: 'platform',
    canActivate: [ownerOnly],
    loadComponent: () => import('./features/shell/shell').then(m => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', loadComponent: () => import('./features/platform/overview').then(m => m.PlatformOverviewComponent) },
      { path: 'academies', loadComponent: () => import('./features/platform/academies').then(m => m.AcademiesComponent) },
      { path: 'academies/:id', loadComponent: () => import('./features/platform/academy-detail').then(m => m.AcademyDetailComponent) },
      { path: 'plans', loadComponent: () => import('./features/platform/plans').then(m => m.PlansComponent) },
      { path: 'invoices', loadComponent: () => import('./features/platform/invoices').then(m => m.InvoicesComponent) },
      { path: 'analytics', loadComponent: () => import('./features/platform/analytics').then(m => m.PlatformAnalyticsComponent) },
      { path: 'audit', loadComponent: () => import('./features/academy/audit').then(m => m.AuditComponent), data: { platform: true } },
    ],
  },
  { path: '**', redirectTo: '' },
];
