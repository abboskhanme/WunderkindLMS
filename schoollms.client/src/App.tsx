import { Suspense, lazy } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { AppLayout } from '@/components/layout/AppLayout'
import { ProtectedRoute, RootRedirect } from '@/components/auth/ProtectedRoute'
import { RequirePerm } from '@/components/auth/RequirePerm'
import { CategoriesPage } from '@/pages/admin/billing/CategoriesPage'
import { BillingSettingsPage } from '@/pages/admin/billing/BillingSettingsPage'
import { RefundsPage } from '@/pages/admin/finance/RefundsPage'
import { PnlExpectationPage } from '@/pages/admin/finance/PnlExpectationPage'
import { IntegrationsSettingsPage } from '@/pages/admin/settings/IntegrationsSettingsPage'
import { GeneralSettingsPage } from '@/pages/admin/settings/GeneralSettingsPage'
import { AdjustmentsPage } from '@/pages/admin/hr/AdjustmentsPage'
import { SubscriptionsPage } from '@/pages/admin/billing/SubscriptionsPage'
import { DiscountsPage } from '@/pages/admin/billing/DiscountsPage'
import { ExpensesPage } from '@/pages/admin/billing/ExpensesPage'
import { CashierPage } from '@/pages/cashier/CashierPage'
import { FinanceView } from '@/pages/portal/FinanceView'
import { LoginPage } from '@/pages/LoginPage'
import { AdminHome } from '@/pages/admin/AdminHome'
import { LeadsPage } from '@/pages/admin/leads/LeadsPage'
import { StudentsPage } from '@/pages/admin/students/StudentsPage'
import { StudentEvaluationPage } from '@/pages/admin/students/StudentEvaluationPage'
import { EvaluationTypesPage } from '@/pages/admin/students/EvaluationTypesPage'
import { StudentDetailPage } from '@/pages/admin/students/StudentDetailPage'
import { StudentTurnstilePage } from '@/pages/admin/students/StudentTurnstilePage'
import { TeacherAttendancePage } from '@/pages/admin/teachers/TeacherAttendancePage'
import { ClassesPage } from '@/pages/admin/classes/ClassesPage'
import { ClassDetailPage } from '@/pages/admin/classes/ClassDetailPage'
import { ClassRatingPage } from '@/pages/admin/classes/ClassRatingPage'
import { ClassSchedulePage } from '@/pages/admin/classes/ClassSchedulePage'
import { TemplateEditorPage } from '@/pages/admin/classes/TemplateEditorPage'
import { SchedulePage } from '@/pages/admin/schedule/SchedulePage'
import { SalaryCalcPage } from '@/pages/admin/schedule/SalaryCalcPage'
import { TeacherSchedulePage } from '@/pages/admin/schedule/TeacherSchedulePage'
import { ClassScheduleViewPage } from '@/pages/admin/schedule/ClassScheduleViewPage'
import { HolidaysPage } from '@/pages/admin/schedule/HolidaysPage'
import { BallarNazoratiPage } from '@/pages/admin/discipline/BallarNazoratiPage'
import { BallSabablarPage } from '@/pages/admin/discipline/BallSabablarPage'
import { GradesReportPage } from '@/pages/admin/grades-report/GradesReportPage'
import { TeacherReportsPage } from '@/pages/admin/teacher-reports/TeacherReportsPage'
import { ContractsPage } from '@/pages/admin/contracts/ContractsPage'
import { BranchesPage } from '@/pages/admin/branches/BranchesPage'
import { EmployeesPage, TeachersRedirect } from '@/pages/admin/staff/EmployeesPage'
import { RolesPage } from '@/pages/admin/staff/RolesPage'
import { AiConnectionsPage } from '@/pages/admin/ai/AiConnectionsPage'
import { FeedbackPage } from '@/pages/admin/feedback/FeedbackPage'
import { AcademicYearPage } from '@/pages/admin/academic-year/AcademicYearPage'
import { SubjectsPage } from '@/pages/admin/subjects/SubjectsPage'
import { JournalPage } from '@/pages/admin/journal/JournalPage'
import { BoardingAttendancePage } from '@/pages/admin/attendance/BoardingAttendancePage'
import { SeasonalMarksPage } from '@/pages/admin/seasonal-marks/SeasonalMarksPage'
import { BySubjectsPage } from '@/pages/admin/seasonal-marks/BySubjectsPage'
import { SeasonalEntryPage } from '@/pages/admin/seasonal-marks/SeasonalEntryPage'
import { CoverageReportPage } from '@/pages/admin/seasonal-marks/CoverageReportPage'
import { JournalClassesPage } from '@/pages/admin/journal/JournalClassesPage'
import { JournalSubjectsPage } from '@/pages/admin/journal/JournalSubjectsPage'
import { MessagesPage } from '@/pages/admin/messages/MessagesPage'
import { AssignmentsPage } from '@/pages/admin/assignments/AssignmentsPage'
import { AssignmentScoresPage } from '@/pages/admin/assignment-scores/AssignmentScoresPage'
import { LmsClassesPage } from '@/pages/admin/lms/LmsClassesPage'
import { LmsSubjectsPage } from '@/pages/admin/lms/LmsSubjectsPage'
import { LmsModulesPage } from '@/pages/admin/lms/LmsModulesPage'
import { LmsTopicsPage } from '@/pages/admin/lms/LmsTopicsPage'
import { AttendancePage } from '@/pages/admin/attendance/AttendancePage'
import { LocationPage } from '@/pages/admin/locations/LocationPage'
import { GpsPage } from '@/pages/admin/gps/GpsPage'
import { CamerasPage } from '@/pages/admin/cameras/CamerasPage'
import { ParentsPage } from '@/pages/admin/parents/ParentsPage'
import { TeacherAppPage } from '@/pages/admin/parents/TeacherAppPage'
import { CanteenPage } from '@/pages/admin/canteen/CanteenPage'
import { FinancePage } from '@/pages/admin/finance/FinancePage'
import { ArrearsPage } from '@/pages/admin/finance/ArrearsPage'
import { CashDayPage } from '@/pages/admin/finance/CashDayPage'
import { FinancialReportsPage } from '@/pages/admin/finance/FinancialReportsPage'
import { DebtorStatusesPage } from '@/pages/admin/finance/DebtorStatusesPage'
import { LeadFunnelPage } from '@/pages/admin/leads-funnel/LeadFunnelPage'
import { SurveysPage } from '@/pages/admin/marketing/SurveysPage'
import { SubmissionsPage } from '@/pages/admin/marketing/SubmissionsPage'
import { NewsPage } from '@/pages/admin/marketing/NewsPage'
import { SurveyPage } from '@/pages/public/SurveyPage'
import { ReceiptVerifyPage } from '@/pages/public/ReceiptVerifyPage'
import { PortalNewsPage } from '@/pages/portal/PortalNewsPage'
import { CandidatesPage } from '@/pages/admin/admission/CandidatesPage'
import { CandidateCardPage } from '@/pages/admin/admission/CandidateCardPage'
import { BanksPage } from '@/pages/admin/admission/BanksPage'
import { BankDetailPage } from '@/pages/admin/admission/BankDetailPage'
import { ExamsPage } from '@/pages/admin/exams/ExamsPage'
import { ExamTypesPage } from '@/pages/admin/exams/ExamTypesPage'
import { ResultsPage } from '@/pages/admin/exams/ResultsPage'
import { ResultEntryPage } from '@/pages/admin/exams/ResultEntryPage'
import { AttendanceDisciplineReportPage } from '@/pages/admin/discipline/AttendanceDisciplineReportPage'
import { CertificatesPage } from '@/pages/admin/certificates/CertificatesPage'
import { GroupsPage } from '@/pages/admin/groups/GroupsPage'
import { RoomsPage } from '@/pages/admin/rooms/RoomsPage'
import { TransactionsPage } from '@/pages/admin/finance/TransactionsPage'
import { InvoicesPage } from '@/pages/admin/billing/InvoicesPage'
import { StudentStatusesPage } from '@/pages/admin/students/StudentStatusesPage'
import { GroupFormPage } from '@/pages/admin/groups/GroupFormPage'
import { GroupRosterPage } from '@/pages/admin/groups/GroupRosterPage'
import { ClassRosterPage } from '@/pages/admin/classes/ClassRosterPage'
import { CertificateTypesPage } from '@/pages/admin/certificates/CertificateTypesPage'
import { HarakatlarPage } from '@/pages/admin/discipline/HarakatlarPage'
import { AttendanceAnalyticsPage } from '@/pages/admin/attendance/AttendanceAnalyticsPage'
import { DailyMarkingPage } from '@/pages/admin/attendance/DailyMarkingPage'
import { TurnstileAnalyticsPage } from '@/pages/admin/turnstile/TurnstileAnalyticsPage'
import { TurnstileFlowPage } from '@/pages/admin/turnstile/TurnstileFlowPage'
import { DailyAttendanceReportPage } from '@/pages/admin/turnstile/DailyAttendanceReportPage'
import { SettingsPage } from '@/pages/admin/settings/SettingsPage'
import { AccountPage } from '@/pages/admin/account/AccountPage'
import { TeacherAppRedirect } from '@/components/TeacherAppRedirect'
import { Loader } from '@/components/ui/Loader'

// P1-26 — Pul aylanmasi (3D halqa). LAZY: sahifa Three.js'ga tayanadi, statik
// import qilinsa u butun ilovaning asosiy bundle'iga tushardi.
const MoneyFlowPage = lazy(() => import('@/pages/admin/finance/MoneyFlowPage'))

export default function App() {
  return (
    <Routes>
      {/* Ochiq sahifa */}
      <Route path="/login" element={<LoginPage />} />
      {/* Ommaviy ariza formasi — tizimga kirmagan ota-ona uchun; o'z to'liq ekrani bor.
          Server `/ariza/` ni HAR QANDAY hostda shu SPA'ga beradi (sales-marketing.md D2). */}
      <Route path="/ariza/:slug" element={<SurveyPage />} />
      {/* Chek QR kodi (2026-09-24) — login'siz. */}
      <Route path="/r/:token" element={<ReceiptVerifyPage />} />

      <Route path="/" element={<RootRedirect />} />

      {/* Administrator paneli */}
      <Route element={<ProtectedRoute role="admin" />}>
        <Route path="/admin" element={<AppLayout />}>
          <Route index element={<AdminHome />} />
          <Route path="leads" element={<RequirePerm perm="leads"><LeadsPage /></RequirePerm>} />
          <Route path="leads/funnel" element={<RequirePerm perm="leads"><LeadFunnelPage /></RequirePerm>} />
          {/* `key` — ikki yo'l bitta komponent: menyudan o'tilganda sahifa qayta yaratilsin,
              aks holda ichki "Faol/Arxiv" holati eski qolib, ro'yxat almashmasdi. */}
          <Route path="students" element={<RequirePerm perm="students"><StudentsPage key="active" /></RequirePerm>} />
          <Route path="students/baholash" element={<RequirePerm perm="students"><StudentEvaluationPage /></RequirePerm>} />
          <Route path="students/baholash-turlari" element={<RequirePerm perm="students"><EvaluationTypesPage /></RequirePerm>} />
          <Route path="students/turniket" element={<RequirePerm perm="students"><StudentTurnstilePage /></RequirePerm>} />
          <Route path="students/arxiv" element={<RequirePerm perm="students"><StudentsPage key="archived" initialTab="archived" /></RequirePerm>} />
          <Route path="students/holatlar" element={<RequirePerm perm="students"><StudentStatusesPage /></RequirePerm>} />
          <Route path="students/turniket/analitika" element={<RequirePerm perm="students"><TurnstileAnalyticsPage /></RequirePerm>} />
          <Route path="students/turniket/kirish-chiqish" element={<RequirePerm perm="students"><TurnstileFlowPage /></RequirePerm>} />
          <Route path="students/turniket/kunlik-davomat" element={<RequirePerm perm="students"><DailyAttendanceReportPage /></RequirePerm>} />
          <Route path="students/:id" element={<RequirePerm perm="students"><StudentDetailPage /></RequirePerm>} />
          {/* O'qituvchilar ro'yxati Boshqaruv → Xodimlar ga qo'shildi (2026-09-26). Eski manzil
              (dashboard, global qidiruv `?q=&tab=`) o'sha sahifaga `?position=teacher` bilan o'tadi. */}
          <Route path="teachers" element={<TeachersRedirect />} />
          <Route path="teachers/attendance" element={<RequirePerm perm="teachers"><TeacherAttendancePage /></RequirePerm>} />
          <Route path="classes" element={<RequirePerm perm="classes"><ClassesPage /></RequirePerm>} />
          <Route path="classes/rating" element={<RequirePerm perm="classes"><ClassRatingPage /></RequirePerm>} />
          <Route path="classes/:id" element={<RequirePerm perm="classes"><ClassDetailPage /></RequirePerm>} />
          <Route path="classes/:id/roster" element={<RequirePerm perm="classes"><ClassRosterPage /></RequirePerm>} />
          {/* Kechki dars va yotoqxona davomati (2026-09-23). Ruxsat — sessiya bo'yicha, sahifa ichida. */}
          <Route path="attendance/boarding" element={<BoardingAttendancePage />} />
          {/* Imtihonlar — mavsumiy baholash (mijoz, 2026-09-23: menyuga ulandi). */}
          <Route path="seasonal-marks" element={<RequirePerm perm="seasonalMarks"><SeasonalMarksPage /></RequirePerm>} />
          <Route path="seasonal-marks/by-subjects" element={<RequirePerm perm="seasonalMarks"><BySubjectsPage /></RequirePerm>} />
          <Route path="seasonal-marks/entry" element={<RequirePerm perm="seasonalMarks"><SeasonalEntryPage /></RequirePerm>} />
          <Route path="seasonal-marks/report" element={<RequirePerm perm="seasonalMarks"><CoverageReportPage /></RequirePerm>} />
          <Route path="groups" element={<RequirePerm perm="classes"><GroupsPage /></RequirePerm>} />
          <Route path="groups/new" element={<RequirePerm perm="classes"><GroupFormPage /></RequirePerm>} />
          <Route path="groups/:id" element={<RequirePerm perm="classes"><GroupFormPage /></RequirePerm>} />
          <Route path="groups/:id/students" element={<RequirePerm perm="classes"><GroupRosterPage /></RequirePerm>} />
          <Route path="schedule" element={<RequirePerm perm="schedule"><ClassScheduleViewPage /></RequirePerm>} />
          <Route path="schedule/teachers" element={<RequirePerm perm="schedule"><TeacherSchedulePage /></RequirePerm>} />
          <Route path="schedule/manage" element={<RequirePerm perm="schedule"><SchedulePage /></RequirePerm>} />
          {/* Ish haqi — Moliya ostida va endi har bir xodim uchun: `finance` ham ochadi (sahifa o'zi
              hasFinanceAccess bilan tekshiradi). */}
          <Route path="teachers/salary" element={<RequirePerm perm={['finance', 'teachers']}><SalaryCalcPage /></RequirePerm>} />
          <Route path="schedule/holidays" element={<RequirePerm perm="schedule"><HolidaysPage /></RequirePerm>} />
          <Route path="discipline" element={<RequirePerm perm="discipline"><BallarNazoratiPage /></RequirePerm>} />
          <Route path="discipline/reasons" element={<RequirePerm perm="discipline"><BallSabablarPage /></RequirePerm>} />
          <Route path="discipline/incidents" element={<RequirePerm perm="discipline"><HarakatlarPage /></RequirePerm>} />
          <Route path="discipline/attendance-report" element={<RequirePerm perm="discipline"><AttendanceDisciplineReportPage /></RequirePerm>} />
          <Route path="schedule/manage/:id" element={<RequirePerm perm="schedule"><ClassSchedulePage /></RequirePerm>} />
          <Route path="schedule/manage/:id/template/:templateId" element={<RequirePerm perm="schedule"><TemplateEditorPage /></RequirePerm>} />
          <Route path="subjects" element={<RequirePerm perm="students"><SubjectsPage /></RequirePerm>} />
          <Route path="rooms" element={<RequirePerm perm="students"><RoomsPage /></RequirePerm>} />
          {/* Jurnal → sinf → fan (EduSchool oqimi, 2026-09-23). */}
          <Route path="journal" element={<RequirePerm perm="journal"><JournalClassesPage /></RequirePerm>} />
          <Route path="journal/:classId" element={<RequirePerm perm="journal"><JournalSubjectsPage /></RequirePerm>} />
          <Route path="journal/:classId/:subjectId" element={<RequirePerm perm="journal"><JournalPage /></RequirePerm>} />
          <Route path="assignments" element={<RequirePerm perm="app"><AssignmentsPage /></RequirePerm>} />
          <Route path="assignment-scores" element={<RequirePerm perm="app"><AssignmentScoresPage /></RequirePerm>} />
          <Route path="lms" element={<RequirePerm perm="app"><LmsClassesPage /></RequirePerm>} />
          <Route path="lms/:classId" element={<RequirePerm perm="app"><LmsSubjectsPage /></RequirePerm>} />
          <Route path="lms/:classId/:subjectId" element={<RequirePerm perm="app"><LmsModulesPage /></RequirePerm>} />
          <Route path="lms/:classId/:subjectId/:moduleId" element={<RequirePerm perm="app"><LmsTopicsPage /></RequirePerm>} />
          <Route path="messages" element={<RequirePerm perm="messages"><MessagesPage /></RequirePerm>} />
          <Route path="grades-report" element={<Navigate to="/admin/grades-report/school" replace />} />
          <Route path="grades-report/:section" element={<RequirePerm perm="gradesReport"><GradesReportPage /></RequirePerm>} />
          <Route path="teacher-reports" element={<RequirePerm perm="teacherReports"><TeacherReportsPage /></RequirePerm>} />
          <Route path="contracts" element={<RequirePerm perm="contracts"><ContractsPage /></RequirePerm>} />
          <Route path="certificates" element={<RequirePerm perm="students"><CertificatesPage /></RequirePerm>} />
          <Route path="certificates/types" element={<RequirePerm perm="students"><CertificateTypesPage /></RequirePerm>} />
          <Route path="attendance" element={<RequirePerm perm="attendance"><AttendancePage /></RequirePerm>} />
          <Route path="attendance/analytics" element={<RequirePerm perm="attendance"><AttendanceAnalyticsPage /></RequirePerm>} />
          {/* Davomat BELGILASH — mas'ul xodim ekrani (mijoz, 2026-09-18).
              Ruxsat o'sha `attendance`: xodimga shu bitta ruxsat beriladi. */}
          <Route path="attendance/mark" element={<RequirePerm perm="attendance"><DailyMarkingPage /></RequirePerm>} />
          <Route path="locations" element={<RequirePerm perm="students"><LocationPage /></RequirePerm>} />
          <Route path="parents" element={<RequirePerm perm="students"><ParentsPage /></RequirePerm>} />
          <Route path="app/teachers" element={<RequirePerm perm="app"><TeacherAppPage /></RequirePerm>} />
          <Route path="canteen" element={<RequirePerm perm="app"><CanteenPage /></RequirePerm>} />
          <Route path="finance" element={<RequirePerm perm="finance"><FinancePage key="finance" /></RequirePerm>} />
          {/* Rol tekshiruvi sahifaning ichida (SPEC §4.3: faqat admin/direktor) */}
          <Route path="finance/money-flow" element={<Suspense fallback={<Loader label="Yuklanmoqda…" />}><MoneyFlowPage /></Suspense>} />
          {/* Rol tekshiruvi sahifaning ichida (SPEC §4.3: faqat admin/direktor) */}
          <Route path="finance/arrears" element={<ArrearsPage />} />
          <Route path="finance/debtor-statuses" element={<DebtorStatusesPage />} />
          <Route path="finance/cash-day" element={<CashDayPage />} />
          <Route path="finance/reports" element={<FinancialReportsPage />} />
          <Route path="finance/transactions" element={<RequirePerm perm="finance"><TransactionsPage /></RequirePerm>} />
          <Route path="billing/invoices" element={<RequirePerm perm="finance"><InvoicesPage /></RequirePerm>} />
          <Route path="billing/categories" element={<RequirePerm perm="finance"><CategoriesPage /></RequirePerm>} />
          <Route path="billing/settings" element={<RequirePerm perm="finance"><BillingSettingsPage /></RequirePerm>} />
          <Route path="finance/refunds" element={<RequirePerm perm="finance"><RefundsPage /></RequirePerm>} />
          {/* EduSchool'da alohida menyu yozuvi — bizda bitta sahifaning tablari. */}
          <Route path="finance/debtors" element={<RequirePerm perm="finance"><FinancePage key="finance-debtors" initialTab="debtors" /></RequirePerm>} />
          <Route path="finance/pnl" element={<RequirePerm perm="finance"><FinancePage key="finance-pnl" initialTab="pnl" /></RequirePerm>} />
          <Route path="finance/pnl-2" element={<RequirePerm perm="finance"><PnlExpectationPage /></RequirePerm>} />
          <Route path="finance/cashflow" element={<RequirePerm perm="finance"><FinancePage key="finance-cashflow" initialTab="cashflow" /></RequirePerm>} />
          {/* Sozlamalar EduSchool tuzilishida: to'rtta sahifa, har biri ichida bo'limlar. */}
          <Route path="settings/integrations" element={<RequirePerm perm="settings"><IntegrationsSettingsPage /></RequirePerm>} />
          <Route path="settings/general" element={<RequirePerm perm="settings"><GeneralSettingsPage /></RequirePerm>} />
          {/* Sotuv va marketing — ariza formalari, topshirilganlar, yangiliklar. */}
          <Route path="marketing/arizalar" element={<RequirePerm perm="marketing"><SurveysPage /></RequirePerm>} />
          <Route path="marketing/topshirilganlar" element={<RequirePerm perm="marketing"><SubmissionsPage /></RequirePerm>} />
          <Route path="marketing/yangiliklar" element={<RequirePerm perm="marketing"><NewsPage /></RequirePerm>} />
          {/* Qabul va Blok Test (admission-and-testing.md). */}
          <Route path="admission/candidates" element={<RequirePerm perm="admission"><CandidatesPage /></RequirePerm>} />
          <Route path="admission/candidates/:leadId" element={<RequirePerm perm="admission"><CandidateCardPage /></RequirePerm>} />
          <Route path="admission/banks" element={<RequirePerm perm="admission"><BanksPage /></RequirePerm>} />
          <Route path="admission/banks/:bankId" element={<RequirePerm perm="admission"><BankDetailPage /></RequirePerm>} />
          <Route path="exams/list" element={<RequirePerm perm="exams"><ExamsPage /></RequirePerm>} />
          <Route path="exams/types" element={<RequirePerm perm="exams"><ExamTypesPage /></RequirePerm>} />
          <Route path="exams/results" element={<RequirePerm perm="exams"><ResultsPage /></RequirePerm>} />
          <Route path="exams/results/:examId/entry" element={<RequirePerm perm="exams"><ResultEntryPage /></RequirePerm>} />
          <Route path="finance/bonus" element={<RequirePerm perm="finance"><AdjustmentsPage key="finance-bonus" kind="bonus" /></RequirePerm>} />
          <Route path="finance/penalty" element={<RequirePerm perm="finance"><AdjustmentsPage key="finance-penalty" kind="penalty" /></RequirePerm>} />
          <Route path="billing/subscriptions" element={<RequirePerm perm="finance"><SubscriptionsPage /></RequirePerm>} />
          <Route path="billing/discounts" element={<RequirePerm perm="finance"><DiscountsPage /></RequirePerm>} />
          <Route path="billing/expenses" element={<RequirePerm perm="finance"><ExpensesPage /></RequirePerm>} />
          <Route path="academic-year" element={<RequirePerm perm="academicYear"><AcademicYearPage /></RequirePerm>} />
          <Route path="settings" element={<Navigate to="/admin/settings/school" replace />} />
          <Route path="settings/:section" element={<RequirePerm perm="settings"><SettingsPage /></RequirePerm>} />
          <Route path="account" element={<AccountPage />} />

          {/* Boshqaruv */}
          <Route path="boshqaruv/gps" element={<RequirePerm perm="gps"><GpsPage /></RequirePerm>} />
          <Route path="boshqaruv/cameras" element={<RequirePerm perm="cameras"><CamerasPage /></RequirePerm>} />
          {/* Xodimlar — o'qituvchilar + boshqa xodimlar bitta ro'yxatda; sahifa ruxsati yo'q qismini yashiradi. */}
          <Route path="boshqaruv/staff" element={<RequirePerm perm={['staff', 'teachers']}><EmployeesPage /></RequirePerm>} />
          <Route path="boshqaruv/feedback" element={<RequirePerm perm="feedback"><FeedbackPage /></RequirePerm>} />
          {/* Rollar — alohida sahifa (ruxsatlar rolga beriladi) */}
          <Route path="boshqaruv/roles" element={<RequirePerm perm="staff"><RolesPage /></RequirePerm>} />
          <Route element={<ProtectedRoute role="superadmin" />}>
            <Route path="boshqaruv/branches" element={<BranchesPage />} />
            <Route path="boshqaruv/ai" element={<AiConnectionsPage />} />
          </Route>
        </Route>
      </Route>

      {/* Kassa — kassir ish o'rni (P1-16). ATAYLAB /admin daraxtidan tashqarida:
          u yerdagi role="admin" darvozasi `staff` ni kiritib, `cashier` ni
          chiqarib yuboradi — bu yerda aynan teskarisi kerak. */}
      <Route element={<ProtectedRoute roles={['cashier', 'admin', 'superadmin', 'staff']} />}>
        <Route path="/cashier" element={<AppLayout />}>
          <Route index element={<CashierPage />} />
        </Route>
      </Route>

      {/* Ota-ona va o'quvchi portali — hozircha moliya ko'rinishi. */}
      <Route element={<ProtectedRoute role="parent" />}>
        <Route path="/parent" element={<AppLayout />}>
          <Route index element={<FinanceView />} />
          <Route path="yangiliklar" element={<PortalNewsPage />} />
        </Route>
      </Route>
      <Route element={<ProtectedRoute role="student" />}>
        <Route path="/student" element={<AppLayout />}>
          <Route index element={<FinanceView />} />
          <Route path="yangiliklar" element={<PortalNewsPage />} />
        </Route>
      </Route>

      {/* O'qituvchi — alohida o'rnatiladigan PWA (/teacher/, wwwroot/teacher statik ilova).
          SPA shu manzilga kelsa (login redirect / RootRedirect) to'liq sahifa bilan o'sha ilovaga o'tamiz. */}
      <Route path="/teacher/*" element={<TeacherAppRedirect />} />

      <Route path="*" element={<RootRedirect />} />
    </Routes>
  )
}
