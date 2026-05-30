import { Navigate, Route, Routes } from 'react-router-dom'
import { AppLayout } from '@/components/layout/AppLayout'
import { ProtectedRoute, RootRedirect } from '@/components/auth/ProtectedRoute'
import { RequirePerm } from '@/components/auth/RequirePerm'
import { LoginPage } from '@/pages/LoginPage'
import { AdminDashboard } from '@/pages/admin/AdminDashboard'
import { LeadsPage } from '@/pages/admin/leads/LeadsPage'
import { StudentsPage } from '@/pages/admin/students/StudentsPage'
import { TeachersPage } from '@/pages/admin/teachers/TeachersPage'
import { ClassesPage } from '@/pages/admin/classes/ClassesPage'
import { ClassDetailPage } from '@/pages/admin/classes/ClassDetailPage'
import { ClassRatingPage } from '@/pages/admin/classes/ClassRatingPage'
import { ClassSchedulePage } from '@/pages/admin/classes/ClassSchedulePage'
import { TemplateEditorPage } from '@/pages/admin/classes/TemplateEditorPage'
import { SchedulePage } from '@/pages/admin/schedule/SchedulePage'
import { TeacherSchedulePage } from '@/pages/admin/schedule/TeacherSchedulePage'
import { ClassScheduleViewPage } from '@/pages/admin/schedule/ClassScheduleViewPage'
import { GradesReportPage } from '@/pages/admin/grades-report/GradesReportPage'
import { TeacherReportsPage } from '@/pages/admin/teacher-reports/TeacherReportsPage'
import { ContractsPage } from '@/pages/admin/contracts/ContractsPage'
import { BranchesPage } from '@/pages/admin/branches/BranchesPage'
import { StaffPage } from '@/pages/admin/staff/StaffPage'
import { RolesPage } from '@/pages/admin/roles/RolesPage'
import { FeedbackPage } from '@/pages/admin/feedback/FeedbackPage'
import { AcademicYearPage } from '@/pages/admin/academic-year/AcademicYearPage'
import { SubjectsPage } from '@/pages/admin/subjects/SubjectsPage'
import { JournalPage } from '@/pages/admin/journal/JournalPage'
import { MessagesPage } from '@/pages/admin/messages/MessagesPage'
import { AssignmentsPage } from '@/pages/admin/assignments/AssignmentsPage'
import { AssignmentScoresPage } from '@/pages/admin/assignment-scores/AssignmentScoresPage'
import { LmsClassesPage } from '@/pages/admin/lms/LmsClassesPage'
import { LmsSubjectsPage } from '@/pages/admin/lms/LmsSubjectsPage'
import { LmsTopicsPage } from '@/pages/admin/lms/LmsTopicsPage'
import { AttendancePage } from '@/pages/admin/attendance/AttendancePage'
import { LocationPage } from '@/pages/admin/locations/LocationPage'
import { ParentsPage } from '@/pages/admin/parents/ParentsPage'
import { CanteenPage } from '@/pages/admin/canteen/CanteenPage'
import { FinancePage } from '@/pages/admin/finance/FinancePage'
import { SettingsPage } from '@/pages/admin/settings/SettingsPage'
import { AccountPage } from '@/pages/admin/account/AccountPage'
import { TeacherDashboard } from '@/pages/teacher/TeacherDashboard'
import { TeacherAssignmentsPage } from '@/pages/teacher/assignments/AssignmentsPage'
import { TeacherJournalPage } from '@/pages/teacher/journal/JournalPage'
import { TeacherSchedulePage as TeacherMySchedulePage } from '@/pages/teacher/schedule/SchedulePage'
import { TeacherMessagesPage } from '@/pages/teacher/messages/MessagesPage'
import { TeacherSalaryPage } from '@/pages/teacher/salary/SalaryPage'
import { TeacherLmsPage } from '@/pages/teacher/lms/TeacherLmsPage'
import { TeacherLmsSubjectPage } from '@/pages/teacher/lms/TeacherLmsSubjectPage'
import { ComingSoon } from '@/pages/ComingSoon'

export default function App() {
  return (
    <Routes>
      {/* Ochiq sahifa */}
      <Route path="/login" element={<LoginPage />} />

      <Route path="/" element={<RootRedirect />} />

      {/* Administrator paneli */}
      <Route element={<ProtectedRoute role="admin" />}>
        <Route path="/admin" element={<AppLayout />}>
          <Route index element={<AdminDashboard />} />
          <Route path="leads" element={<RequirePerm perm="leads"><LeadsPage /></RequirePerm>} />
          <Route path="students" element={<RequirePerm perm="students"><StudentsPage /></RequirePerm>} />
          <Route path="teachers" element={<RequirePerm perm="teachers"><TeachersPage /></RequirePerm>} />
          <Route path="classes" element={<RequirePerm perm="classes"><ClassesPage /></RequirePerm>} />
          <Route path="classes/rating" element={<RequirePerm perm="classes"><ClassRatingPage /></RequirePerm>} />
          <Route path="classes/:id" element={<RequirePerm perm="classes"><ClassDetailPage /></RequirePerm>} />
          <Route path="schedule" element={<RequirePerm perm="schedule"><ClassScheduleViewPage /></RequirePerm>} />
          <Route path="schedule/teachers" element={<RequirePerm perm="schedule"><TeacherSchedulePage /></RequirePerm>} />
          <Route path="schedule/manage" element={<RequirePerm perm="schedule"><SchedulePage /></RequirePerm>} />
          <Route path="schedule/manage/:id" element={<RequirePerm perm="schedule"><ClassSchedulePage /></RequirePerm>} />
          <Route path="schedule/manage/:id/template/:templateId" element={<RequirePerm perm="schedule"><TemplateEditorPage /></RequirePerm>} />
          <Route path="subjects" element={<RequirePerm perm="schedule"><SubjectsPage /></RequirePerm>} />
          <Route path="journal" element={<RequirePerm perm="journal"><JournalPage /></RequirePerm>} />
          <Route path="assignments" element={<RequirePerm perm="app"><AssignmentsPage /></RequirePerm>} />
          <Route path="assignment-scores" element={<RequirePerm perm="app"><AssignmentScoresPage /></RequirePerm>} />
          <Route path="lms" element={<RequirePerm perm="app"><LmsClassesPage /></RequirePerm>} />
          <Route path="lms/:classId" element={<RequirePerm perm="app"><LmsSubjectsPage /></RequirePerm>} />
          <Route path="lms/:classId/:subjectId" element={<RequirePerm perm="app"><LmsTopicsPage /></RequirePerm>} />
          <Route path="messages" element={<RequirePerm perm="messages"><MessagesPage /></RequirePerm>} />
          <Route path="grades-report" element={<Navigate to="/admin/grades-report/school" replace />} />
          <Route path="grades-report/:section" element={<RequirePerm perm="gradesReport"><GradesReportPage /></RequirePerm>} />
          <Route path="teacher-reports" element={<RequirePerm perm="teacherReports"><TeacherReportsPage /></RequirePerm>} />
          <Route path="contracts" element={<RequirePerm perm="contracts"><ContractsPage /></RequirePerm>} />
          <Route path="attendance" element={<RequirePerm perm="attendance"><AttendancePage /></RequirePerm>} />
          <Route path="locations" element={<RequirePerm perm="app"><LocationPage /></RequirePerm>} />
          <Route path="parents" element={<RequirePerm perm="app"><ParentsPage /></RequirePerm>} />
          <Route path="canteen" element={<RequirePerm perm="app"><CanteenPage /></RequirePerm>} />
          <Route path="finance" element={<RequirePerm perm="finance"><FinancePage /></RequirePerm>} />
          <Route path="academic-year" element={<RequirePerm perm="academicYear"><AcademicYearPage /></RequirePerm>} />
          <Route path="settings" element={<Navigate to="/admin/settings/school" replace />} />
          <Route path="settings/:section" element={<RequirePerm perm="settings"><SettingsPage /></RequirePerm>} />
          <Route path="account" element={<AccountPage />} />

          {/* Boshqaruv */}
          <Route path="boshqaruv/staff" element={<RequirePerm perm="staff"><StaffPage /></RequirePerm>} />
          <Route path="boshqaruv/feedback" element={<RequirePerm perm="feedback"><FeedbackPage /></RequirePerm>} />
          <Route element={<ProtectedRoute role="superadmin" />}>
            <Route path="boshqaruv/branches" element={<BranchesPage />} />
            <Route path="boshqaruv/roles" element={<RolesPage />} />
          </Route>
        </Route>
      </Route>

      {/* O'qituvchi paneli */}
      <Route element={<ProtectedRoute role="teacher" />}>
        <Route path="/teacher" element={<AppLayout />}>
          <Route index element={<TeacherDashboard />} />
          <Route
            path="journal"
            element={
              <RequirePerm perm="journal">
                <TeacherJournalPage />
              </RequirePerm>
            }
          />
          <Route
            path="assignments"
            element={
              <RequirePerm perm="assignments">
                <TeacherAssignmentsPage />
              </RequirePerm>
            }
          />
          <Route
            path="schedule"
            element={
              <RequirePerm perm="schedule">
                <TeacherMySchedulePage />
              </RequirePerm>
            }
          />
          <Route
            path="messages"
            element={
              <RequirePerm perm="messages">
                <TeacherMessagesPage />
              </RequirePerm>
            }
          />
          <Route
            path="salary"
            element={
              <RequirePerm perm="salary">
                <TeacherSalaryPage />
              </RequirePerm>
            }
          />
          <Route path="lms" element={<TeacherLmsPage />} />
          <Route path="lms/:subjectId" element={<TeacherLmsSubjectPage />} />
        </Route>
      </Route>

      {/* O'quvchi paneli */}
      <Route element={<ProtectedRoute role="student" />}>
        <Route path="/student" element={<AppLayout />}>
          <Route index element={<ComingSoon title="O'quvchi paneli" />} />
        </Route>
      </Route>

      {/* Ota-ona paneli */}
      <Route element={<ProtectedRoute role="parent" />}>
        <Route path="/parent" element={<AppLayout />}>
          <Route index element={<ComingSoon title="Ota-ona paneli" />} />
        </Route>
      </Route>

      <Route path="*" element={<RootRedirect />} />
    </Routes>
  )
}
